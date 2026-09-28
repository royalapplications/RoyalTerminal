// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <appmodel.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <propkey.h>
#include <propvarutil.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <shlwapi.h>
#include <wincodec.h>
#include <sddl.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Data.Xml.Dom.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Notifications.h>
#include <filesystem>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <cwctype>
#include <fstream>
#include <functional>
#include <memory>
#include <string>
#include <string_view>
#include <unordered_set>
#include <vector>

namespace rt_notifications {
using winrt::Windows::Data::Json::JsonObject;
using winrt::Windows::Data::Json::JsonArray;
using winrt::Windows::Data::Xml::Dom::XmlDocument;
using winrt::Windows::Data::Xml::Dom::XmlElement;
using namespace winrt::Windows::UI::Notifications;
using feedback = std::function<void(std::string, int)>;

inline std::wstring text(JsonObject const& value, wchar_t const* key) { return std::wstring(value.GetNamedString(key, L"")); }
inline std::string token(JsonObject const& value, wchar_t const* key) {
    auto s = winrt::to_string(value.GetNamedString(key, L""));
    return s.size() == 32 && s.find_first_not_of("0123456789abcdef") == std::string::npos ? s : std::string{};
}
inline std::wstring uuid() {
    GUID id{}; winrt::check_hresult(CoCreateGuid(&id));
    wchar_t buffer[40]{}; StringFromGUID2(id, buffer, 40);
    std::wstring value;
    for (wchar_t c : std::wstring_view(buffer)) if (iswxdigit(c)) value += static_cast<wchar_t>(towlower(c));
    return value;
}
inline bool icon_name(std::wstring const& name) {
    return !name.empty() && name.size() <= 256 && name != L"." && name != L".." &&
        name.find_first_not_of(L"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-!") == std::wstring::npos;
}

// The actor owns the platform and every lease. Only feedback may run elsewhere.
struct toast_lease { virtual ~toast_lease() = default; virtual void close(bool remove) noexcept = 0; };
struct notification_platform {
    virtual ~notification_platform() = default;
    virtual void initialize() = 0;
    virtual bool available() = 0;
    virtual std::unique_ptr<toast_lease> show(JsonObject const&, std::string const& tag, feedback) = 0;
    virtual std::unordered_set<std::string> history() = 0;
};

inline std::filesystem::path known_folder(REFKNOWNFOLDERID id) {
    PWSTR raw{}; winrt::check_hresult(SHGetKnownFolderPath(id, 0, nullptr, &raw));
    std::unique_ptr<wchar_t, decltype(&CoTaskMemFree)> owned(raw, CoTaskMemFree);
    return std::filesystem::path(raw);
}

inline std::wstring executable() {
    std::wstring result(32768, L'\0');
    DWORD size = GetModuleFileNameW(nullptr, result.data(), static_cast<DWORD>(result.size()));
    if (!size || size >= result.size()) throw winrt::hresult_error(E_FAIL);
    result.resize(size); return result;
}

inline std::wstring application_id(std::wstring const& path) {
    // Stable per host executable; never take an identity or command from OSC data.
    std::wstring normalized = path;
    for (auto& c : normalized) c = static_cast<wchar_t>(towlower(c));
    unsigned char hash[32]{};
    if (BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0,
        reinterpret_cast<PUCHAR>(normalized.data()), static_cast<ULONG>(normalized.size() * sizeof(wchar_t)), hash, sizeof(hash)) < 0)
        throw winrt::hresult_error(E_FAIL);
    std::wstring result = L"RoyalApps.RoyalTerminal.";
    constexpr wchar_t hex[] = L"0123456789abcdef";
    for (unsigned char byte : hash) { result += hex[byte >> 4]; result += hex[byte & 15]; }
    return result;
}

inline void ensure_shortcut(std::wstring const& appid, std::wstring const& exe) {
    auto destination = known_folder(FOLDERID_Programs) / (L"RoyalTerminal notifications (" + appid.substr(appid.size() - 12) + L").lnk");
    auto matches = [&] {
        auto link = winrt::create_instance<IShellLinkW>(CLSID_ShellLink);
        auto file = link.as<IPersistFile>();
        if (FAILED(file->Load(destination.c_str(), STGM_READ))) return false;
        wchar_t target[32768]{};
        if (FAILED(link->GetPath(target, 32768, nullptr, SLGP_RAWPATH)) || _wcsicmp(target, exe.c_str()) != 0) return false;
        PROPVARIANT value{};
        HRESULT hr = link.as<IPropertyStore>()->GetValue(PKEY_AppUserModel_ID, &value);
        bool equal = SUCCEEDED(hr) && value.vt == VT_LPWSTR && value.pwszVal && appid == value.pwszVal;
        PropVariantClear(&value); return equal;
    };
    if (std::filesystem::exists(destination)) {
        if (!matches()) throw winrt::hresult_error(E_ACCESSDENIED); // Never overwrite a user's shortcut.
        return;
    }
    auto link = winrt::create_instance<IShellLinkW>(CLSID_ShellLink);
    winrt::check_hresult(link->SetPath(exe.c_str()));
    winrt::check_hresult(link->SetArguments(L""));
    winrt::check_hresult(link->SetDescription(L"RoyalTerminal desktop notification identity"));
    auto properties = link.as<IPropertyStore>();
    PROPVARIANT value{}; winrt::check_hresult(InitPropVariantFromString(appid.c_str(), &value));
    HRESULT hr = properties->SetValue(PKEY_AppUserModel_ID, value); PropVariantClear(&value); winrt::check_hresult(hr);
    winrt::check_hresult(properties->Commit());
    auto temporary = destination.parent_path() / (L"RoyalTerminal-" + uuid() + L".lnk");
    try {
        winrt::check_hresult(link.as<IPersistFile>()->Save(temporary.c_str(), TRUE));
        if (!MoveFileExW(temporary.c_str(), destination.c_str(), 0) && !matches()) throw winrt::hresult_error(E_ACCESSDENIED);
    } catch (...) { DeleteFileW(temporary.c_str()); throw; }
    DeleteFileW(temporary.c_str());
}

inline bool valid_png(std::vector<unsigned char> const& bytes) {
    constexpr unsigned char signature[]{137, 80, 78, 71, 13, 10, 26, 10};
    if (bytes.size() < 24 || bytes.size() > 5 * 1024 * 1024 || memcmp(bytes.data(), signature, 8) != 0) return false;
    auto dimension = [&](size_t at) { return (uint32_t(bytes[at]) << 24) | (uint32_t(bytes[at + 1]) << 16) | (uint32_t(bytes[at + 2]) << 8) | bytes[at + 3]; };
    uint32_t w = dimension(16), h = dimension(20);
    return w && h && w <= 2048 && h <= 2048 && uint64_t(w) * h <= 1024 * 1024;
}

inline bool named_icon(std::wstring const& name, std::filesystem::path const& file) {
    if (!icon_name(name)) return false;
    winrt::com_ptr<IWICBitmap> bitmap;
    auto wic = winrt::create_instance<IWICImagingFactory>(CLSID_WICImagingFactory);
    SHSTOCKICONID stock = SIID_INVALID;
    if (name == L"error") stock = SIID_ERROR;
    else if (name == L"warn" || name == L"warning") stock = SIID_WARNING;
    else if (name == L"info" || name == L"question" || name == L"help") stock = SIID_INFO;
    else if (name == L"file-manager") stock = SIID_FOLDER;
    else if (name == L"text-editor") stock = SIID_DOCNOASSOC;
    else if (name == L"system-monitor") stock = SIID_APPLICATION;
    if (stock != SIID_INVALID) {
        SHSTOCKICONINFO info{sizeof(info)};
        if (FAILED(SHGetStockIconInfo(stock, SHGSI_ICON | SHGSI_LARGEICON, &info))) return false;
        HRESULT hr = wic->CreateBitmapFromHICON(info.hIcon, bitmap.put()); DestroyIcon(info.hIcon);
        if (FAILED(hr)) return false;
    } else {
        // Fixed local AppsFolder namespace. No caller path, URL, shell verb or launch.
        winrt::com_ptr<IShellItemImageFactory> item;
        auto path = L"shell:AppsFolder\\" + name;
        if (FAILED(SHCreateItemFromParsingName(path.c_str(), nullptr, IID_PPV_ARGS(item.put())))) return false;
        HBITMAP handle{};
        if (FAILED(item->GetImage(SIZE{256,256}, SIIGBF_ICONONLY, &handle))) return false;
        HRESULT hr = wic->CreateBitmapFromHBITMAP(handle, nullptr, WICBitmapUseAlpha, bitmap.put()); DeleteObject(handle);
        if (FAILED(hr)) return false;
    }
    winrt::com_ptr<IWICStream> stream; winrt::check_hresult(wic->CreateStream(stream.put()));
    winrt::check_hresult(stream->InitializeFromFilename(file.c_str(), GENERIC_WRITE));
    winrt::com_ptr<IWICBitmapEncoder> encoder; winrt::check_hresult(wic->CreateEncoder(GUID_ContainerFormatPng, nullptr, encoder.put()));
    winrt::check_hresult(encoder->Initialize(stream.get(), WICBitmapEncoderNoCache));
    winrt::com_ptr<IWICBitmapFrameEncode> frame; winrt::check_hresult(encoder->CreateNewFrame(frame.put(), nullptr));
    winrt::check_hresult(frame->Initialize(nullptr)); winrt::check_hresult(frame->WriteSource(bitmap.get(), nullptr));
    winrt::check_hresult(frame->Commit()); winrt::check_hresult(encoder->Commit()); return true;
}

struct image_file {
    std::filesystem::path directory, file;
    ~image_file() { clear(); }
    void clear() noexcept {
        std::error_code ignored;
        if (!file.empty()) std::filesystem::remove(file, ignored);
        if (!directory.empty()) std::filesystem::remove(directory, ignored);
        file.clear(); directory.clear();
    }
    void create(JsonObject const& command) {
        auto names = command.GetNamedArray(L"icons", JsonArray{});
        auto image = text(command, L"image");
        if (!names.Size() && image.empty() && text(command, L"application").empty()) return;
        auto parent = known_folder(FOLDERID_LocalAppData) / L"RoyalApps" / L"RoyalTerminal" / L"NotificationImages";
        std::filesystem::create_directories(parent);
        directory = parent / uuid();
        PSECURITY_DESCRIPTOR descriptor{};
        winrt::check_bool(ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:P(A;;FA;;;SY)(A;;FA;;;OW)", SDDL_REVISION_1, &descriptor, nullptr));
        SECURITY_ATTRIBUTES attributes{sizeof(attributes), descriptor, FALSE};
        BOOL created = CreateDirectoryW(directory.c_str(), &attributes); LocalFree(descriptor); winrt::check_bool(created);
        file = directory / L"image.png";
        for (auto const& name : names) { try { if (named_icon(std::wstring(name.GetString()), file)) return; } catch (...) { } }
        if (!image.empty() && image.size() <= 7 * 1024 * 1024) {
            DWORD size{};
            if (CryptStringToBinaryW(image.data(), static_cast<DWORD>(image.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_STRICT, nullptr, &size, nullptr, nullptr) && size <= 5 * 1024 * 1024) {
                std::vector<unsigned char> bytes(size);
                if (CryptStringToBinaryW(image.data(), static_cast<DWORD>(image.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_STRICT, bytes.data(), &size, nullptr, nullptr) && valid_png(bytes)) {
                    std::ofstream output(file, std::ios::binary); output.write(reinterpret_cast<char const*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
                    if (output.good()) return;
                }
            }
        }
        if (!names.Size() && image.empty()) { try { if (named_icon(text(command, L"application"), file)) return; } catch (...) { } }
        std::error_code ignored; std::filesystem::remove(file, ignored); file.clear();
    }
};

inline XmlDocument content(JsonObject const& command, std::filesystem::path const& image) {
    XmlDocument document;
    document.LoadXml(L"<toast launch=\"--royalterminal-notification\" scenario=\"default\"><visual><binding template=\"ToastGeneric\"/></visual></toast>");
    auto binding = document.GetElementsByTagName(L"binding").Item(0);
    for (auto key : {L"title", L"body"}) {
        auto element = document.CreateElement(L"text"); element.InnerText(text(command, key)); binding.AppendChild(element);
    }
    if (!image.empty()) {
        std::wstring uri(32768, L'\0'); DWORD count = static_cast<DWORD>(uri.size());
        winrt::check_hresult(UrlCreateFromPathW(image.c_str(), uri.data(), &count, 0)); uri.resize(count);
        auto element = document.CreateElement(L"image"); element.SetAttribute(L"placement", L"appLogoOverride"); element.SetAttribute(L"src", uri);
        binding.AppendChild(element);
    }
    if (text(command, L"sound") == L"silent") {
        auto audio = document.CreateElement(L"audio"); audio.SetAttribute(L"silent", L"true"); document.DocumentElement().AppendChild(audio);
    }
    auto buttons = command.GetNamedArray(L"buttons", JsonArray{});
    if (buttons.Size()) {
        auto actions = document.CreateElement(L"actions"); document.DocumentElement().AppendChild(actions);
        for (uint32_t i = 0; i < std::min(5u, buttons.Size()); ++i) {
            auto action = document.CreateElement(L"action"); action.SetAttribute(L"content", buttons.GetStringAt(i));
            action.SetAttribute(L"arguments", L"--royalterminal-notification:" + std::to_wstring(i + 1)); actions.AppendChild(action);
        }
    }
    return document;
}

class windows_platform final : public notification_platform {
    struct lease final : toast_lease {
        ToastNotification toast{nullptr}; ToastNotifier notifier{nullptr};
        std::wstring appid, group, tag; bool packaged{}, retired{};
        winrt::event_token activated{}, failed{};
        image_file image;
        ~lease() override { close(true); }
        void close(bool remove) noexcept override {
            if (retired) return; retired = true;
            try { if (toast && activated.value) toast.Activated(activated); } catch (...) { }
            try { if (toast && failed.value) toast.Failed(failed); } catch (...) { }
            if (remove) {
                try { if (notifier && toast) notifier.Hide(toast); } catch (...) { }
                try { if (packaged) ToastNotificationManager::History().Remove(tag, group); else ToastNotificationManager::History().Remove(tag, group, appid); } catch (...) { }
            }
            toast = nullptr;
        }
    };
    ToastNotifier _notifier{nullptr};
    std::wstring _appid, _group = uuid();
    bool _packaged{};
public:
    void initialize() override {
        UINT32 size{}; _packaged = GetCurrentPackageFullName(&size, nullptr) == ERROR_INSUFFICIENT_BUFFER;
        if (_packaged) _notifier = ToastNotificationManager::CreateToastNotifier();
        else {
            auto exe = executable(); auto stem = std::filesystem::path(exe).stem().wstring();
            for (auto& c : stem) c = static_cast<wchar_t>(towlower(c));
            if (stem == L"dotnet" || stem == L"testhost" || stem == L"vstest.console") throw winrt::hresult_error(E_NOTIMPL);
            _appid = application_id(exe); ensure_shortcut(_appid, exe);
            _notifier = ToastNotificationManager::CreateToastNotifier(_appid);
        }
        _group.resize(16);
    }
    bool available() override { return _notifier && _notifier.Setting() == NotificationSetting::Enabled; }
    std::unique_ptr<toast_lease> show(JsonObject const& command, std::string const& tag, feedback report) override {
        auto result = std::make_unique<lease>(); result->notifier = _notifier; result->appid = _appid;
        result->packaged = _packaged; result->group = _group; result->tag = winrt::to_hstring(tag);
        try { result->image.create(command); } catch (...) { result->image.clear(); /* Keep text if icon preparation fails. */ }
        result->toast = ToastNotification(content(command, result->image.file));
        result->toast.Tag(result->tag); result->toast.Group(_group);
        int urgency = static_cast<int>(command.GetNamedNumber(L"urgency", 1));
        result->toast.SuppressPopup(urgency == 0);
        result->toast.Priority(urgency >= 2 ? ToastNotificationPriority::High : ToastNotificationPriority::Default);
        result->activated = result->toast.Activated([report](auto const&, winrt::Windows::Foundation::IInspectable const& raw) {
            try {
                auto args = raw.try_as<ToastActivatedEventArgs>(); if (!args) return;
                auto argument = args.Arguments();
                if (argument == L"--royalterminal-notification" || argument.empty()) report("activated", 0);
                else {
                    std::wstring_view value(argument), prefix = L"--royalterminal-notification:";
                    if (value.starts_with(prefix) && value.size() == prefix.size() + 1 && value.back() >= L'1' && value.back() <= L'5') report("activated", value.back() - L'0');
                }
            } catch (...) { }
        });
        result->failed = result->toast.Failed([report](auto const&, auto const&) { try { report("failed", 0); } catch (...) { } });
        _notifier.Show(result->toast); return result;
    }
    std::unordered_set<std::string> history() override {
        auto notifications = _packaged ? ToastNotificationManager::History().GetHistory() : ToastNotificationManager::History().GetHistory(_appid);
        std::unordered_set<std::string> tags;
        for (auto const& toast : notifications) if (toast.Group() == _group) tags.insert(winrt::to_string(toast.Tag()));
        return tags;
    }
};
} // namespace rt_notifications
