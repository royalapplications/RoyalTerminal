// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#import <AppKit/AppKit.h>
#import <UserNotifications/UserNotifications.h>
#import <objc/runtime.h>
#include "notifications.h"

@class RTNotificationClient;

@interface RTNotificationEntry : NSObject
@property(copy) NSString *token;
@property(copy) NSString *identifier;
@property(copy) NSDictionary *command;
@property(strong) NSURL *directory;
@property(strong) UNNotificationCategory *category;
@property long long sequence;
@property BOOL cancelled;
@property BOOL submitting;
@property BOOL awaitingAuthorization;
@property BOOL delivered;
@property BOOL reported;
@property NSUInteger misses;
@property NSTimeInterval deliveredAt;
@end
@implementation RTNotificationEntry
@end

// One broker per UN center, attached to that center rather than mutable static
// state. Do not take over another embedding application's notification delegate.
@interface RTNotificationBroker : NSObject <UNUserNotificationCenterDelegate>
@property(strong) UNUserNotificationCenter *center;
@property(strong) NSHashTable<RTNotificationClient *> *clients;
- (void)updateCategories:(void (^)(void))completion;
@end

@interface RTNotificationClient : NSObject
@property(strong) RTNotificationBroker *broker;
@property(strong) NSMutableDictionary<NSString *, RTNotificationEntry *> *entries;
@property(strong) NSMutableArray<NSDictionary *> *events;
@property BOOL ready;
@property BOOL changed;
@property BOOL stopped;
@property BOOL scanQueued;
@property BOOL authorizationPending;
@property NSUInteger submissions;
@property NSInteger capabilities;
@property long long stopSequence;
@property NSTimeInterval lastScan;
- (void)initializeCenter;
- (void)command:(NSDictionary *)command;
- (void)complete:(long long)sequence success:(BOOL)success;
- (void)emit:(NSString *)kind entry:(RTNotificationEntry *)entry button:(NSUInteger)button;
- (void)closeEntry:(RTNotificationEntry *)entry removeDelivered:(BOOL)remove;
- (void)stop;
- (void)scan;
@end

static const char RTBrokerKey = 0;
static NSString *const RTCategoryPrefix = @"RoyalTerminal.Notification.";
// Domain flag values, intentionally no CloseEvents, Urgency or NamedSounds.
// Apple does not promise all close events, nor critical delivery without a
// separate entitlement. System/silent match Ghostty and Kitty's macOS presenter.
static const NSInteger RTCapabilities = 1 | 2 | 16 | 32 | 64 | 128 | 512;

static NSString *RTString(id value) { return [value isKindOfClass:NSString.class] ? value : @""; }
static NSArray *RTArray(id value) { return [value isKindOfClass:NSArray.class] ? value : @[]; }
static long long RTNumber(id value) { return [value isKindOfClass:NSNumber.class] ? [value longLongValue] : 0; }
static BOOL RTToken(NSString *value) {
    if (value.length != 32) return NO;
    return [value rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdef"] invertedSet]].location == NSNotFound;
}
static BOOL RTIconName(NSString *value) {
    if (!value.length || value.length > 256 || [value isEqualToString:@"."] || [value isEqualToString:@".."]) return NO;
    NSMutableCharacterSet *allowed = [NSCharacterSet.alphanumericCharacterSet mutableCopy];
    [allowed addCharactersInString:@"._-"];
    return [value rangeOfCharacterFromSet:allowed.invertedSet].location == NSNotFound;
}

static NSImage *RTNamedIcon(NSString *name) {
    if (!RTIconName(name)) return nil;
    NSString *symbol = @{@"error": @"exclamationmark.octagon.fill", @"warn": @"exclamationmark.triangle.fill",
        @"warning": @"exclamationmark.triangle.fill", @"info": @"info.circle.fill", @"question": @"questionmark.circle.fill",
        @"help": @"questionmark.circle", @"file-manager": @"folder.fill", @"system-monitor": @"chart.bar.fill", @"text-editor": @"doc.text"}[name];
    if (symbol) return [NSImage imageWithSystemSymbolName:symbol accessibilityDescription:nil];
    NSURL *application = [NSWorkspace.sharedWorkspace URLForApplicationWithBundleIdentifier:name];
    return application ? [NSWorkspace.sharedWorkspace iconForFile:application.path] : nil;
}

static NSData *RTPng(NSImage *image) {
    if (!image) return nil;
    NSBitmapImageRep *bitmap = [[NSBitmapImageRep alloc] initWithBitmapDataPlanes:NULL pixelsWide:256 pixelsHigh:256
        bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace bytesPerRow:0 bitsPerPixel:0];
    if (!bitmap) return nil;
    [NSGraphicsContext saveGraphicsState];
    @try {
        NSGraphicsContext.currentContext = [NSGraphicsContext graphicsContextWithBitmapImageRep:bitmap];
        [image drawInRect:NSMakeRect(0, 0, 256, 256) fromRect:NSZeroRect operation:NSCompositingOperationCopy fraction:1];
    } @finally { [NSGraphicsContext restoreGraphicsState]; }
    return [bitmap representationUsingType:NSBitmapImageFileTypePNG properties:@{}];
}

static NSData *RTIconData(RTNotificationEntry *entry) {
    NSData *png = nil;
    NSArray *names = RTArray(entry.command[@"icons"]);
    for (id value in names) {
        png = RTPng(RTNamedIcon(RTString(value)));
        if (png) break;
    }
    // Managed code has decoded a bounded first frame and re-encoded PNG. Never
    // accept a caller path or URL; every attachment filename is locally generated.
    if (!png) png = [[NSData alloc] initWithBase64EncodedString:RTString(entry.command[@"image"]) options:0];
    if (!png.length && names.count == 0) png = RTPng(RTNamedIcon(RTString(entry.command[@"application"])));
    return png;
}

static BOOL RTValidPng(NSData *png) {
    if (png.length < 24 || png.length > 5 * 1024 * 1024) return NO;
    const unsigned char *bytes = png.bytes;
    const unsigned char signature[] = {137, 80, 78, 71, 13, 10, 26, 10};
    if (memcmp(bytes, signature, sizeof(signature)) != 0) return NO;
    uint32_t width = ((uint32_t)bytes[16] << 24) | ((uint32_t)bytes[17] << 16) | ((uint32_t)bytes[18] << 8) | bytes[19];
    uint32_t height = ((uint32_t)bytes[20] << 24) | ((uint32_t)bytes[21] << 16) | ((uint32_t)bytes[22] << 8) | bytes[23];
    return width > 0 && height > 0 && width <= 2048 && height <= 2048 && (uint64_t)width * height <= 1024 * 1024;
}

static void RTAttachImage(UNMutableNotificationContent *content, RTNotificationEntry *entry, NSData *png) {
    if (!RTValidPng(png)) return;
    NSURL *directory = [NSURL fileURLWithPath:[NSTemporaryDirectory() stringByAppendingPathComponent:
        [@"royalterminal-notification-" stringByAppendingString:NSUUID.UUID.UUIDString]] isDirectory:YES];
    if (![NSFileManager.defaultManager createDirectoryAtURL:directory withIntermediateDirectories:NO
        attributes:@{NSFilePosixPermissions: @0700} error:NULL]) return;
    entry.directory = directory;
    NSURL *path = [directory URLByAppendingPathComponent:@"image.png"];
    if (![png writeToURL:path options:NSDataWritingAtomic error:NULL]) return;
    UNNotificationAttachment *attachment = [UNNotificationAttachment attachmentWithIdentifier:@"image" URL:path options:nil error:NULL];
    if (attachment) content.attachments = @[attachment];
}

@implementation RTNotificationBroker
- (instancetype)init {
    if ((self = [super init])) _clients = NSHashTable.weakObjectsHashTable;
    return self;
}
- (void)updateCategories:(void (^)(void))completion {
    @try {
    [self.center getNotificationCategoriesWithCompletionHandler:^(NSSet<UNNotificationCategory *> *existing) {
        dispatch_async(dispatch_get_main_queue(), ^{
            @try {
                // Re-read foreign categories rather than restoring a stale global
                // snapshot. Every queued update computes the latest live union.
                NSMutableSet *categories = [NSMutableSet new];
                for (UNNotificationCategory *category in existing)
                    if (![category.identifier hasPrefix:RTCategoryPrefix]) [categories addObject:category];
                for (RTNotificationClient *client in self.clients)
                    for (RTNotificationEntry *entry in client.entries.allValues)
                        if (!entry.cancelled && entry.category) [categories addObject:entry.category];
                if (self.center.delegate == self) [self.center setNotificationCategories:categories];
            } @catch (__unused NSException *exception) { }
            if (completion) completion();
        });
    }];
    } @catch (__unused NSException *exception) { if (completion) completion(); }
}
- (void)userNotificationCenter:(UNUserNotificationCenter *)center willPresentNotification:(UNNotification *)notification
    withCompletionHandler:(void (^)(UNNotificationPresentationOptions))completion {
    (void)center;
    dispatch_async(dispatch_get_main_queue(), ^{
        BOOL owned = NO;
        NSString *token = RTString(notification.request.content.userInfo[@"royalToken"]);
        for (RTNotificationClient *client in self.clients)
            if (client.entries[token] && !client.entries[token].cancelled) { owned = YES; break; }
        completion(owned ? UNNotificationPresentationOptionBanner | UNNotificationPresentationOptionList | UNNotificationPresentationOptionSound : 0);
    });
}
- (void)userNotificationCenter:(UNUserNotificationCenter *)center didReceiveNotificationResponse:(UNNotificationResponse *)response
    withCompletionHandler:(void (^)(void))completion {
    (void)center;
    dispatch_async(dispatch_get_main_queue(), ^{
        @try {
            NSString *token = RTString(response.notification.request.content.userInfo[@"royalToken"]);
            for (RTNotificationClient *client in self.clients) {
                RTNotificationEntry *entry = client.entries[token];
                if (!entry || entry.cancelled || entry.reported) continue;
                if ([response.actionIdentifier isEqualToString:UNNotificationDismissActionIdentifier])
                    [client emit:@"closed" entry:entry button:0];
                else if ([response.actionIdentifier isEqualToString:UNNotificationDefaultActionIdentifier])
                    [client emit:@"activated" entry:entry button:0];
                else {
                    NSUInteger button = 0;
                    NSArray *actions = entry.category.actions;
                    for (NSUInteger i = 0; i < actions.count; i++)
                        if ([((UNNotificationAction *)actions[i]).identifier isEqualToString:response.actionIdentifier]) { button = i + 1; break; }
                    if (button) [client emit:@"activated" entry:entry button:button];
                }
                break;
            }
        } @catch (__unused NSException *exception) { }
        completion();
    });
}
@end

@implementation RTNotificationClient
- (instancetype)init {
    if ((self = [super init])) { _entries = [NSMutableDictionary new]; _events = [NSMutableArray new]; _changed = YES; }
    return self;
}
- (void)initializeCenter {
    @try {
        UNUserNotificationCenter *center = UNUserNotificationCenter.currentNotificationCenter;
        RTNotificationBroker *broker = objc_getAssociatedObject(center, &RTBrokerKey);
        if (center.delegate && center.delegate != broker) { @synchronized(self) { self.ready = YES; self.changed = YES; } return; }
        if (!broker) {
            broker = [RTNotificationBroker new]; broker.center = center;
            objc_setAssociatedObject(center, &RTBrokerKey, broker, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
            center.delegate = broker;
        }
        self.broker = broker; [broker.clients addObject:self];
        [center getNotificationSettingsWithCompletionHandler:^(UNNotificationSettings *settings) {
            dispatch_async(dispatch_get_main_queue(), ^{
                @synchronized(self) {
                    self.ready = YES; self.changed = YES;
                    self.capabilities = !self.stopped && settings.authorizationStatus != UNAuthorizationStatusDenied ? RTCapabilities : 0;
                }
            });
        }];
    } @catch (__unused NSException *exception) { @synchronized(self) { self.ready = YES; self.changed = YES; } }
}
- (void)complete:(long long)sequence success:(BOOL)success {
    if (sequence <= 0) return;
    @synchronized(self) {
        if (self.events.count < 512) [self.events addObject:@{@"kind": @"operation", @"sequence": @(sequence), @"success": @(success)}];
        else self.capabilities = 0;
        self.changed = YES;
    }
}
- (void)emit:(NSString *)kind entry:(RTNotificationEntry *)entry button:(NSUInteger)button {
    if (entry.reported || entry.cancelled) return;
    entry.reported = YES;
    @synchronized(self) {
        if (self.events.count < 512) [self.events addObject:@{@"kind": kind, @"token": entry.token, @"button": @(button)}];
        else self.capabilities = 0;
        self.changed = YES;
    }
    [self closeEntry:entry removeDelivered:YES];
    [self.broker updateCategories:nil];
}
- (void)closeEntry:(RTNotificationEntry *)entry removeDelivered:(BOOL)remove {
    if (!entry) return;
    entry.cancelled = YES;
    if (remove) {
        [self.broker.center removePendingNotificationRequestsWithIdentifiers:@[entry.identifier]];
        [self.broker.center removeDeliveredNotificationsWithIdentifiers:@[entry.identifier]];
    }
    if (self.entries[entry.token] == entry) [self.entries removeObjectForKey:entry.token];
    if (!entry.submitting && entry.directory) { [NSFileManager.defaultManager removeItemAtURL:entry.directory error:NULL]; entry.directory = nil; }
    if (!entry.submitting) entry.command = @{};
}
- (void)finishStop {
    if (!self.stopped || self.submissions != 0) return;
    [self complete:self.stopSequence success:YES]; self.stopSequence = 0;
    RTNotificationBroker *broker = self.broker;
    [broker.clients removeObject:self];
    [broker updateCategories:^{
        if (broker.clients.count == 0) {
            if (broker.center.delegate == broker) broker.center.delegate = nil;
            if (objc_getAssociatedObject(broker.center, &RTBrokerKey) == broker)
                objc_setAssociatedObject(broker.center, &RTBrokerKey, nil, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
        }
    }];
}
- (void)stop {
    @synchronized(self) { self.stopped = YES; self.capabilities = 0; self.changed = YES; }
    for (RTNotificationEntry *entry in self.entries.allValues) [self closeEntry:entry removeDelivered:YES];
    [self finishStop];
}
- (void)deliveryFinished:(RTNotificationEntry *)entry error:(BOOL)error {
    entry.submitting = NO; self.submissions--;
    if (entry.directory) { [NSFileManager.defaultManager removeItemAtURL:entry.directory error:NULL]; entry.directory = nil; }
    if (self.stopped || entry.cancelled || error) {
        [self closeEntry:entry removeDelivered:YES];
        [self complete:entry.sequence success:NO];
    } else {
        entry.delivered = YES; entry.deliveredAt = NSProcessInfo.processInfo.systemUptime;
        [self complete:entry.sequence success:YES];
    }
    entry.command = @{};
    [self finishStop];
}
- (void)deliver:(RTNotificationEntry *)entry {
    if (self.stopped || entry.cancelled || self.submissions >= 128 || self.broker.center.delegate != self.broker) {
        [self closeEntry:entry removeDelivered:YES]; [self complete:entry.sequence success:NO]; return;
    }
    @try {
        UNMutableNotificationContent *content = [UNMutableNotificationContent new];
        content.title = RTString(entry.command[@"title"]); content.body = RTString(entry.command[@"body"]);
        content.threadIdentifier = RTString(entry.command[@"application"]);
        content.userInfo = @{@"royalToken": entry.token};
        content.categoryIdentifier = entry.category.identifier;
        content.sound = [RTString(entry.command[@"sound"]) isEqualToString:@"silent"] ? nil : UNNotificationSound.defaultSound;
        // Never bypass Do Not Disturb using a critical-alert entitlement.
        content.interruptionLevel = RTNumber(entry.command[@"urgency"]) == 0 ? UNNotificationInterruptionLevelPassive : UNNotificationInterruptionLevelActive;
        NSData *png = RTIconData(entry);
        entry.submitting = YES; self.submissions++;
        // Disk I/O and attachment preparation stay off the AppKit/UI queue.
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
            @autoreleasepool {
                @try { if (!entry.cancelled) RTAttachImage(content, entry, png); }
                @catch (__unused NSException *exception) { }
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (self.stopped || entry.cancelled) { [self deliveryFinished:entry error:YES]; return; }
                    @try {
                        UNNotificationRequest *request = [UNNotificationRequest requestWithIdentifier:entry.identifier content:content trigger:nil];
                        [self.broker.center addNotificationRequest:request withCompletionHandler:^(NSError *error) {
                            dispatch_async(dispatch_get_main_queue(), ^{ [self deliveryFinished:entry error:error != nil]; });
                        }];
                    } @catch (__unused NSException *exception) { [self deliveryFinished:entry error:YES]; }
                });
            }
        });
    } @catch (__unused NSException *exception) {
        [self closeEntry:entry removeDelivered:YES];
        [self complete:entry.sequence success:NO];
    }
}
- (void)authorizeAndDeliver:(RTNotificationEntry *)entry {
    if (self.stopped || entry.cancelled || self.broker.center.delegate != self.broker) {
        [self closeEntry:entry removeDelivered:YES]; [self complete:entry.sequence success:NO]; return;
    }
    @try { [self.broker.center getNotificationSettingsWithCompletionHandler:^(UNNotificationSettings *settings) {
        dispatch_async(dispatch_get_main_queue(), ^{
            @try {
            if (self.stopped || entry.cancelled) { [self complete:entry.sequence success:NO]; return; }
            if (settings.authorizationStatus == UNAuthorizationStatusNotDetermined) {
                // The dialog is requested only by a real delivery, never at startup
                // or by an OSC support query. Managed cancellation does not dismiss
                // or answer the user's OS permission dialog.
                entry.awaitingAuthorization = YES;
                if (self.authorizationPending) return;
                self.authorizationPending = YES;
                [self.broker.center requestAuthorizationWithOptions:UNAuthorizationOptionAlert | UNAuthorizationOptionSound
                    completionHandler:^(BOOL granted, NSError *error) {
                        (void)error;
                        dispatch_async(dispatch_get_main_queue(), ^{
                            self.authorizationPending = NO;
                            if (!granted) {
                                @synchronized(self) { self.capabilities = 0; self.changed = YES; }
                            }
                            // One OS authorization block per client. Cancelled
                            // entries have left the dictionary and cannot be
                            // resurrected when a user eventually grants access.
                            for (RTNotificationEntry *waiting in self.entries.allValues) {
                                if (!waiting.awaitingAuthorization) continue;
                                waiting.awaitingAuthorization = NO;
                                if (granted) [self deliver:waiting];
                                else { [self closeEntry:waiting removeDelivered:YES]; [self complete:waiting.sequence success:NO]; }
                            }
                        });
                    }];
            } else if (settings.authorizationStatus == UNAuthorizationStatusDenied) {
                @synchronized(self) { self.capabilities = 0; self.changed = YES; }
                [self closeEntry:entry removeDelivered:YES];
                [self complete:entry.sequence success:NO];
            } else [self deliver:entry];
            } @catch (__unused NSException *exception) {
                self.authorizationPending = NO;
                [self closeEntry:entry removeDelivered:YES]; [self complete:entry.sequence success:NO];
            }
        });
    }]; } @catch (__unused NSException *exception) {
        [self closeEntry:entry removeDelivered:YES]; [self complete:entry.sequence success:NO];
    }
}
- (void)command:(NSDictionary *)command {
    long long sequence = RTNumber(command[@"sequence"]);
    NSString *operation = RTString(command[@"op"]), *token = RTString(command[@"token"]);
    if ([operation isEqualToString:@"stop"]) { self.stopSequence = sequence; [self stop]; return; }
    if (self.stopped) { [self complete:sequence success:NO]; return; }
    if ([operation isEqualToString:@"close"] && RTToken(token)) {
        [self closeEntry:self.entries[token] removeDelivered:YES];
        [self.broker updateCategories:nil]; [self complete:sequence success:YES]; return;
    }
    if (![operation isEqualToString:@"show"] || !RTToken(token) || self.entries[token] || self.entries.count >= 128 || self.submissions >= 128 || !self.broker ||
        RTString(command[@"title"]).length > 65536 || RTString(command[@"body"]).length > 65536 ||
        RTArray(command[@"buttons"]).count > 32 || RTArray(command[@"icons"]).count > 32) {
        [self complete:sequence success:NO]; return;
    }
    RTNotificationEntry *previous = self.entries[RTString(command[@"replaces"])];
    RTNotificationEntry *entry = [RTNotificationEntry new];
    entry.sequence = sequence;
    BOOL reuseIdentifier = previous && previous.delivered && !previous.submitting;
    entry.token = token; entry.identifier = reuseIdentifier ? previous.identifier : NSUUID.UUID.UUIDString; entry.command = command;
    // If the prior add has not completed, its cancellation cleanup must not be
    // able to delete the replacement. Only completed requests may reuse an ID.
    if (previous) [self closeEntry:previous removeDelivered:!reuseIdentifier];
    self.entries[token] = entry;
    NSMutableArray *actions = [NSMutableArray new];
    NSArray *buttons = RTArray(command[@"buttons"]);
    for (NSUInteger i = 0; i < buttons.count; i++) {
        NSString *identifier = [NSString stringWithFormat:@"%@.%lu", token, (unsigned long)i + 1];
        // No Foreground flag: the shared protocol honors a=-focus before deciding
        // whether to bring the originating pane/window forward.
        [actions addObject:[UNNotificationAction actionWithIdentifier:identifier title:RTString(buttons[i]) options:0]];
    }
    entry.category = [UNNotificationCategory categoryWithIdentifier:[RTCategoryPrefix stringByAppendingString:token]
        actions:actions intentIdentifiers:@[] options:UNNotificationCategoryOptionCustomDismissAction];
    [self.broker updateCategories:^{ [self authorizeAndDeliver:entry]; }];
}
- (void)scan {
    if (self.stopped || !self.broker) { @synchronized(self) { self.scanQueued = NO; } return; }
    if (self.broker.center.delegate != self.broker) {
        @synchronized(self) { self.capabilities = 0; self.changed = YES; self.scanQueued = NO; }
        for (RTNotificationEntry *entry in self.entries.allValues) [self emit:@"failed" entry:entry button:0];
        return;
    }
    if (self.entries.count == 0) { @synchronized(self) { self.scanQueued = NO; } return; }
    NSTimeInterval cutoff = NSProcessInfo.processInfo.systemUptime - 2;
    [self.broker.center getDeliveredNotificationsWithCompletionHandler:^(NSArray<UNNotification *> *notifications) {
        dispatch_async(dispatch_get_main_queue(), ^{
            @synchronized(self) { self.scanQueued = NO; }
            if (self.stopped) return;
            NSMutableSet *tokens = [NSMutableSet new];
            for (UNNotification *notification in notifications) {
                NSString *token = RTString(notification.request.content.userInfo[@"royalToken"]);
                if (RTToken(token)) [tokens addObject:token];
            }
            for (RTNotificationEntry *entry in self.entries.allValues) {
                if (!entry.delivered || entry.deliveredAt > cutoff || entry.cancelled) continue;
                if ([tokens containsObject:entry.token]) entry.misses = 0;
                else if (++entry.misses >= 2) [self emit:@"closed" entry:entry button:0];
            }
        });
    }];
}
@end

void *rt_notifications_create(void) {
    @autoreleasepool { @try {
        NSBundle *bundle = NSBundle.mainBundle;
        if (!bundle.bundleIdentifier.length || ![bundle.bundleURL.pathExtension.lowercaseString isEqualToString:@"app"]) return NULL;
        RTNotificationClient *client = [RTNotificationClient new];
        dispatch_async(dispatch_get_main_queue(), ^{ if (!client.stopped) [client initializeCenter]; });
        return (__bridge_retained void *)client;
    } @catch (__unused NSException *exception) { return NULL; } }
}
int rt_notifications_command(void *handle, const void *bytes, size_t length) {
    if (!handle || !bytes || !length || length > 8 * 1024 * 1024) return 0;
    @autoreleasepool { @try {
        RTNotificationClient *client = (__bridge RTNotificationClient *)handle;
        NSData *data = [NSData dataWithBytes:bytes length:length];
        NSDictionary *command = [NSJSONSerialization JSONObjectWithData:data options:0 error:NULL];
        if (![command isKindOfClass:NSDictionary.class]) return 0;
        dispatch_async(dispatch_get_main_queue(), ^{
            @try { [client command:command]; }
            @catch (__unused NSException *exception) { [client complete:RTNumber(command[@"sequence"]) success:NO]; }
        });
        return 1;
    } @catch (__unused NSException *exception) { return 0; } }
}
void *rt_notifications_poll(void *handle, size_t *length) {
    if (!handle || !length) return NULL;
    *length = 0;
    @autoreleasepool { @try {
        RTNotificationClient *client = (__bridge RTNotificationClient *)handle;
        NSData *data = nil;
        @synchronized(client) {
            NSTimeInterval now = NSProcessInfo.processInfo.systemUptime;
            if (!client.stopped && !client.scanQueued && now - client.lastScan >= 1) {
                client.scanQueued = YES; client.lastScan = now;
                dispatch_async(dispatch_get_main_queue(), ^{ @try { [client scan]; } @catch (__unused NSException *exception) { @synchronized(client) { client.scanQueued = NO; } } });
            }
            if (!client.changed) return NULL;
            data = [NSJSONSerialization dataWithJSONObject:@{@"ready": @(client.ready), @"capabilities": @(client.capabilities), @"events": client.events} options:0 error:NULL];
            if (!data || data.length > 256 * 1024) return NULL;
            void *result = malloc(data.length);
            if (!result) return NULL;
            memcpy(result, data.bytes, data.length); *length = data.length;
            [client.events removeAllObjects]; client.changed = NO;
            return result;
        }
    } @catch (__unused NSException *exception) { return NULL; } }
}
void rt_notifications_free(void *bytes) { free(bytes); }
void rt_notifications_destroy(void *handle) {
    if (!handle) return;
    @autoreleasepool {
        RTNotificationClient *client = (__bridge_transfer RTNotificationClient *)handle;
        dispatch_async(dispatch_get_main_queue(), ^{ @try { [client stop]; } @catch (__unused NSException *exception) { } });
    }
}
