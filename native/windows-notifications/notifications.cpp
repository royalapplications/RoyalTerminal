// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
#include "notification-platform.h"
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <thread>
#include <unordered_map>
#include <cmath>

namespace rt_notifications {
using notification_clock = std::chrono::steady_clock;
constexpr int capabilities = 1 | 2 | 16 | 32 | 64 | 128 | 256 | 512;
struct event { std::string kind, token; long long sequence{}; int button{}; bool success{}; };

class client final : public std::enable_shared_from_this<client> {
    struct entry { std::string tag; unsigned buttons{}, misses{}; notification_clock::time_point delivered; std::unique_ptr<toast_lease> lease; };
    std::mutex _mutex;
    std::condition_variable _wake;
    std::deque<std::string> _commands;
    std::deque<event> _signals, _events;
    size_t _queuedBytes{};
    bool _stop{}, _ready{}, _changed{true};
    int _capabilities{};
    std::unordered_map<std::string, entry> _entries; // Actor only, including leases.
    std::shared_ptr<notification_platform> _platform;

    void emit(event value) {
        std::lock_guard lock(_mutex);
        if (_events.size() < 512) _events.push_back(std::move(value));
        else { _stop = true; _capabilities = 0; }
        _changed = true;
    }
    void complete(long long sequence, bool success) { if (sequence > 0) emit({"operation", {}, sequence, 0, success}); }
    void state(int value) { std::lock_guard lock(_mutex); if (!_ready || _capabilities != value) _changed = true; _ready = true; _capabilities = value; }
    void close(std::string const& id, bool remove = true) {
        auto found = _entries.find(id);
        if (found == _entries.end()) return;
        found->second.lease->close(remove); _entries.erase(found);
    }
    void signal(event value) {
        std::lock_guard lock(_mutex);
        if (_stop) return;
        if (_signals.size() < 512) _signals.push_back(std::move(value));
        else { _stop = true; _capabilities = 0; _changed = true; }
        _wake.notify_one();
    }
    void process_signal(event const& value) {
        auto found = _entries.find(value.token);
        if (found == _entries.end() || value.button < 0 || static_cast<unsigned>(value.button) > found->second.buttons) return;
        emit(value); close(value.token);
    }
    void command(std::string const& bytes) {
        long long sequence = 0;
        try {
            auto value = JsonObject::Parse(winrt::to_hstring(bytes));
            double number = value.GetNamedNumber(L"sequence", 0);
            if (!std::isfinite(number) || number < 0 || number > 9007199254740991.0 || std::floor(number) != number) throw winrt::hresult_invalid_argument();
            sequence = static_cast<long long>(number);
            auto op = text(value, L"op");
            if (op == L"stop") {
                for (auto& pair : _entries) pair.second.lease->close(true);
                _entries.clear(); state(0); complete(sequence, true); request_stop(); return;
            }
            auto id = token(value, L"token");
            if (id.empty()) throw winrt::hresult_invalid_argument();
            if (op == L"close") { close(id); complete(sequence, true); return; }
            auto buttons = value.GetNamedArray(L"buttons", JsonArray{});
            if (op != L"show" || _entries.size() >= 128 || _entries.contains(id) || !_platform->available() ||
                text(value, L"title").size() > 65536 || text(value, L"body").size() > 65536 ||
                buttons.Size() > 5 || value.GetNamedArray(L"icons", JsonArray{}).Size() > 32) throw winrt::hresult_invalid_argument();
            std::string previous = token(value, L"replaces");
            auto prior = _entries.find(previous);
            std::string tag = prior == _entries.end() ? winrt::to_string(uuid().substr(0, 16)) : prior->second.tag;
            std::weak_ptr<client> owner = shared_from_this();
            auto lease = _platform->show(value, tag, [owner, id](std::string kind, int button) {
                if (kind != "activated" && kind != "failed") return;
                if (auto current = owner.lock()) current->signal({std::move(kind), id, 0, button, false});
            });
            if (!lease) throw winrt::hresult_error(E_FAIL);
            // Retirement never removes the new toast which now owns the same tag.
            close(previous, false);
            _entries.emplace(id, entry{tag, buttons.Size(), 0, notification_clock::now(), std::move(lease)});
            complete(sequence, true);
        } catch (...) { complete(sequence, false); }
    }
    void scan() {
        bool enabled = _platform->available(); state(enabled ? capabilities : 0);
        if (_entries.empty()) return;
        if (!enabled) {
            for (auto const& pair : _entries) emit({"failed", pair.first});
            _entries.clear(); return;
        }
        auto tags = _platform->history();
        std::vector<std::string> closed;
        for (auto& pair : _entries) {
            auto& value = pair.second;
            if (tags.contains(value.tag)) value.misses = 0;
            else if (notification_clock::now() - value.delivered >= std::chrono::seconds(2) && ++value.misses >= 2) closed.push_back(pair.first);
        }
        for (auto const& id : closed) { emit({"closed", id}); close(id); }
    }
public:
    explicit client(std::shared_ptr<notification_platform> platform) : _platform(std::move(platform)) { }
    void request_stop() { std::lock_guard lock(_mutex); _stop = true; _wake.notify_one(); }
    bool enqueue(char const* bytes, size_t count) {
        std::lock_guard lock(_mutex);
        if (_stop || _commands.size() >= 128 || count > 8 * 1024 * 1024 - _queuedBytes) return false;
        _commands.emplace_back(bytes, count); _queuedBytes += count; _wake.notify_one(); return true;
    }
    void run() noexcept {
        bool apartment = false;
        try {
            winrt::init_apartment(winrt::apartment_type::multi_threaded); apartment = true;
            if (!_platform) _platform = std::make_shared<windows_platform>();
            _platform->initialize(); state(_platform->available() ? capabilities : 0);
            auto nextScan = notification_clock::now() + std::chrono::seconds(1);
            while (true) {
                std::string bytes; std::deque<event> signals;
                {
                    std::unique_lock lock(_mutex);
                    _wake.wait_until(lock, nextScan, [&] { return _stop || !_commands.empty() || !_signals.empty(); });
                    if (_stop) break;
                    signals.swap(_signals);
                    if (!_commands.empty()) { bytes = std::move(_commands.front()); _commands.pop_front(); _queuedBytes -= bytes.size(); }
                }
                for (auto const& value : signals) process_signal(value);
                if (!bytes.empty()) command(bytes);
                if (notification_clock::now() >= nextScan) { scan(); nextScan = notification_clock::now() + std::chrono::seconds(1); }
            }
        } catch (...) {
            try { for (auto const& pair : _entries) emit({"failed", pair.first}); } catch (...) { }
        }
        // Revoke native event handlers and remove only this client's live toasts.
        _entries.clear(); _platform.reset();
        { std::lock_guard lock(_mutex); _stop = true; _commands.clear(); _signals.clear(); _queuedBytes = 0; }
        state(0);
        if (apartment) winrt::uninit_apartment();
    }
    void* poll(size_t& count) {
        std::lock_guard lock(_mutex);
        if (!_changed) return nullptr;
        // Only generated enums, hexadecimal tokens and numbers enter this JSON.
        std::string result = std::string("{\"ready\":") + (_ready ? "true" : "false") + ",\"capabilities\":" + std::to_string(_capabilities) + ",\"events\":[";
        bool separator = false;
        for (auto const& e : _events) {
            if (separator) result += ','; separator = true;
            result += "{\"kind\":\"" + e.kind + "\",\"token\":\"" + e.token + "\",\"sequence\":" + std::to_string(e.sequence) +
                ",\"button\":" + std::to_string(e.button) + ",\"success\":" + (e.success ? "true" : "false") + "}";
        }
        result += "]}";
        void* memory = malloc(result.size());
        if (!memory) return nullptr;
        memcpy(memory, result.data(), result.size()); count = result.size();
        _events.clear(); _changed = false; return memory;
    }
};

struct handle {
    std::shared_ptr<client> owner;
    std::shared_ptr<std::atomic<bool>> finished = std::make_shared<std::atomic<bool>>(false);
    std::thread worker;
    explicit handle(std::shared_ptr<notification_platform> platform = {}) : owner(std::make_shared<client>(std::move(platform))) {
        worker = std::thread([state = owner, done = finished] { state->run(); done->store(true); });
    }
    ~handle() {
        owner->request_stop();
        // Normal stop is acknowledged after lease cleanup. If an OS call exceeds
        // managed shutdown's deadline, native ownership survives without CLR
        // callbacks. Never block a window close indefinitely on Windows RPC.
        if (worker.joinable()) { if (finished->load()) worker.join(); else worker.detach(); }
    }
};
}

extern "C" __declspec(dllexport) void* rt_notifications_create() noexcept {
    try {
        // Late native RPC/event cleanup may outlive the last managed handle.
        HMODULE module{};
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&rt_notifications_create), &module)) return nullptr;
        return new rt_notifications::handle();
    } catch (...) { return nullptr; }
}
extern "C" __declspec(dllexport) int rt_notifications_command(void* value, void const* bytes, size_t count) noexcept {
    if (!value || !bytes || !count || count > 8 * 1024 * 1024) return 0;
    try { return static_cast<rt_notifications::handle*>(value)->owner->enqueue(static_cast<char const*>(bytes), count) ? 1 : 0; } catch (...) { return 0; }
}
extern "C" __declspec(dllexport) void* rt_notifications_poll(void* value, size_t* count) noexcept {
    if (!value || !count) return nullptr; *count = 0;
    try {
        return static_cast<rt_notifications::handle*>(value)->owner->poll(*count);
    } catch (...) { return nullptr; }
}
extern "C" __declspec(dllexport) void rt_notifications_free(void* bytes) noexcept { free(bytes); }
extern "C" __declspec(dllexport) void rt_notifications_destroy(void* value) noexcept { try { delete static_cast<rt_notifications::handle*>(value); } catch (...) { } }
