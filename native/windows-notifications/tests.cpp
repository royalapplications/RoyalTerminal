// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Fake platform only: never register a shortcut, display a toast or open images.
#include "notifications.cpp"
#include <cassert>

using namespace rt_notifications;
struct fake_platform final : notification_platform {
    struct data { std::string tag; feedback callback; bool closed{}, removed{}; };
    struct fake_lease final : toast_lease {
        std::shared_ptr<data> state;
        explicit fake_lease(std::shared_ptr<data> value) : state(std::move(value)) { }
        ~fake_lease() override { close(true); }
        void close(bool remove) noexcept override { if (!state->closed) { state->closed = true; state->removed = remove; } }
    };
    std::mutex mutex;
    std::condition_variable condition;
    std::vector<std::shared_ptr<data>> delivered;
    std::atomic<bool> enabled{true}, visible{true};
    bool block{}, release{}, inside{}, fail{};
    void initialize() override { }
    bool available() override { return enabled.load(); }
    std::unique_ptr<toast_lease> show(JsonObject const&, std::string const& tag, feedback callback) override {
        std::unique_lock lock(mutex); inside = true; condition.notify_all();
        if (block) condition.wait(lock, [&] { return release; });
        if (fail) throw winrt::hresult_error(E_FAIL);
        auto state = std::make_shared<data>(data{tag, std::move(callback)}); delivered.push_back(state);
        return std::make_unique<fake_lease>(state);
    }
    std::unordered_set<std::string> history() override {
        std::lock_guard lock(mutex); std::unordered_set<std::string> result;
        for (auto const& entry : delivered) if (visible.load() && !entry->closed) result.insert(entry->tag);
        return result;
    }
    void unblock() { std::lock_guard lock(mutex); release = true; condition.notify_all(); }
};

static void wait_for(std::function<bool()> const& condition) {
    auto deadline = notification_clock::now() + std::chrono::seconds(5);
    while (notification_clock::now() < deadline) {
        if (condition()) return;
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
    assert(false && "native notification wait timed out");
}
static void send(handle* owner, std::string text) { assert(rt_notifications_command(owner, text.data(), text.size()) == 1); }
static std::string show(std::string const& token, int sequence, std::string const& replaces = "") {
    return "{\"op\":\"show\",\"token\":\"" + token + "\",\"replaces\":\"" + replaces + "\",\"sequence\":" + std::to_string(sequence) + ",\"buttons\":[\"yes\",\"no\"]}";
}
static JsonObject next(handle* owner, std::function<bool(JsonObject const&)> const& predicate) {
    JsonObject matched{nullptr};
    wait_for([&] {
        size_t count{}; void* raw = rt_notifications_poll(owner, &count); if (!raw) return false;
        std::string bytes(static_cast<char*>(raw), count); rt_notifications_free(raw);
        auto value = JsonObject::Parse(winrt::to_hstring(bytes));
        if (predicate(value)) { matched = value; return true; } return false;
    }); return matched;
}
static bool operation(JsonObject const& state, int sequence, bool success = true) {
    for (auto const& raw : state.GetNamedArray(L"events")) {
        auto value = raw.GetObject();
        if (value.GetNamedString(L"kind") == L"operation" && value.GetNamedNumber(L"sequence") == sequence && value.GetNamedBoolean(L"success") == success) return true;
    } return false;
}
static void test_replacement() {
    auto platform = std::make_shared<fake_platform>(); auto owner = std::make_unique<handle>(platform);
    std::string first(32, '1'), second(32, '2');
    send(owner.get(), show(first, 1)); next(owner.get(), [](auto const& value) { return operation(value, 1); });
    send(owner.get(), show(second, 2, first)); next(owner.get(), [](auto const& value) { return operation(value, 2); });
    assert(platform->delivered.size() == 2);
    assert(platform->delivered[0]->tag == platform->delivered[1]->tag);
    assert(platform->delivered[0]->closed && !platform->delivered[0]->removed);
    platform->delivered[0]->callback("activated", 0); // Superseded action cannot route to a new pane.
    platform->delivered[1]->callback("activated", 3); // Out-of-range button is ignored.
    platform->delivered[1]->callback("activated", 2);
    auto state = next(owner.get(), [](auto const& value) { return value.GetNamedArray(L"events").Size() > 0; });
    auto events = state.GetNamedArray(L"events"); assert(events.Size() == 1);
    auto activated = events.GetObjectAt(0);
    assert(activated.GetNamedString(L"token") == winrt::to_hstring(second));
    assert(activated.GetNamedNumber(L"button") == 2);
    send(owner.get(), "{\"op\":\"stop\",\"sequence\":3}"); next(owner.get(), [](auto const& value) { return operation(value, 3); });
    wait_for([&] { return owner->finished->load(); });
    assert(platform->delivered[1]->closed && platform->delivered[1]->removed);
}
static void test_native_late_cleanup() {
    auto platform = std::make_shared<fake_platform>(); platform->block = true;
    auto owner = std::make_unique<handle>(platform); auto finished = owner->finished;
    send(owner.get(), show(std::string(32, '3'), 4));
    { std::unique_lock lock(platform->mutex); assert(platform->condition.wait_for(lock, std::chrono::seconds(5), [&] { return platform->inside; })); }
    owner.reset(); // Managed handle can end while the OS call is still in flight.
    platform->unblock(); wait_for([&] { return finished->load(); });
    assert(platform->delivered.size() == 1 && platform->delivered[0]->removed);
    platform->delivered[0]->callback("activated", 0); // Stopped/expired weak owner; no CLR callback exists.
}
static void test_xml_and_bounds() {
    auto command = JsonObject::Parse(L"{\"title\":\"<x>&%\",\"body\":\"literal\",\"sound\":\"silent\",\"buttons\":[\"a<&\",\"b\",\"c\",\"d\",\"e\",\"f\"]}");
    auto document = content(command, {});
    assert(document.GetElementsByTagName(L"text").Item(0).InnerText() == L"<x>&%");
    assert(document.GetElementsByTagName(L"action").Length() == 5);
    assert(document.GetElementsByTagName(L"action").Item(0).as<XmlElement>().GetAttribute(L"arguments") == L"--royalterminal-notification:1");
    assert(document.DocumentElement().GetAttribute(L"launch") == L"--royalterminal-notification");
    assert(document.GetElementsByTagName(L"audio").Item(0).as<XmlElement>().GetAttribute(L"silent") == L"true");
    assert(!icon_name(L"file:///x") && !icon_name(L"../image") && icon_name(L"Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
    assert(application_id(L"C:\\App\\terminal.exe") == application_id(L"c:\\app\\TERMINAL.EXE"));
    assert(application_id(L"C:\\one.exe") != application_id(L"C:\\two.exe"));
    assert(!valid_png({}));
    std::vector<unsigned char> png(24); const unsigned char signature[]{137,80,78,71,13,10,26,10};
    memcpy(png.data(), signature, 8); png[19] = 1; png[23] = 1; assert(valid_png(png));
    png[16] = 127; assert(!valid_png(png));
    auto platform = std::make_shared<fake_platform>(); auto owner = std::make_unique<handle>(platform);
    assert(rt_notifications_command(owner.get(), "{}", 9 * 1024 * 1024) == 0);
    send(owner.get(), "{\"op\":\"show\",\"sequence\":5,\"token\":\"not-a-token\"}");
    next(owner.get(), [](auto const& value) { return operation(value, 5, false); });
    platform->fail = true;
    send(owner.get(), show(std::string(32, '4'), 6)); next(owner.get(), [](auto const& value) { return operation(value, 6, false); });
    assert(platform->delivered.empty());
}
static void test_history_and_disabled_settings() {
    auto platform = std::make_shared<fake_platform>(); auto owner = std::make_unique<handle>(platform);
    send(owner.get(), show(std::string(32, '5'), 7)); next(owner.get(), [](auto const& value) { return operation(value, 7); });
    platform->visible.store(false);
    auto closed = next(owner.get(), [](auto const& value) {
        for (auto const& raw : value.GetNamedArray(L"events")) if (raw.GetObject().GetNamedString(L"kind") == L"closed") return true;
        return false;
    });
    assert(closed.GetNamedArray(L"events").GetObjectAt(0).GetNamedString(L"token") == std::wstring(32, L'5'));
    platform->visible.store(true);
    send(owner.get(), show(std::string(32, '6'), 8)); next(owner.get(), [](auto const& value) { return operation(value, 8); });
    platform->enabled.store(false);
    auto disabled = next(owner.get(), [](auto const& value) {
        if (value.GetNamedNumber(L"capabilities") != 0) return false;
        for (auto const& raw : value.GetNamedArray(L"events")) if (raw.GetObject().GetNamedString(L"kind") == L"failed") return true;
        return false;
    });
    assert(disabled.GetNamedArray(L"events").GetObjectAt(0).GetNamedString(L"kind") == L"failed");
}
int main() {
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    test_replacement(); test_native_late_cleanup(); test_xml_and_bounds(); test_history_and_disabled_settings();
    winrt::uninit_apartment(); return 0;
}
