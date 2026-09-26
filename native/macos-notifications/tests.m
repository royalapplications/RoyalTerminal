// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Unit tests use a fake notification center. No permission prompt or desktop
// delivery is possible: initializeCenter/currentNotificationCenter is never used.
#include "notifications.m"
#include <assert.h>

@interface RTTestSettings : NSObject
@property UNAuthorizationStatus authorizationStatus;
@end
@implementation RTTestSettings
@end

@interface RTTestNotification : NSObject
@property(strong) UNNotificationRequest *request;
@end
@implementation RTTestNotification
@end

@interface RTTestResponse : NSObject
@property(strong) RTTestNotification *notification;
@property(copy) NSString *actionIdentifier;
@end
@implementation RTTestResponse
@end

@interface RTTestCenter : NSObject
@property(weak) id<UNUserNotificationCenterDelegate> delegate;
@property(strong) NSMutableArray<UNNotificationRequest *> *requests;
@property(strong) NSMutableArray *completions;
@property(strong) NSMutableArray<NSString *> *removed;
@property(strong) NSSet<UNNotificationCategory *> *categories;
@property NSUInteger authorizationRequests;
@property UNAuthorizationStatus authorization;
@property BOOL holdAuthorization;
@property(copy) void (^authorizationCompletion)(BOOL, NSError *);
@end
@implementation RTTestCenter
- (instancetype)init {
    if ((self = [super init])) {
        _requests = [NSMutableArray new]; _completions = [NSMutableArray new]; _removed = [NSMutableArray new];
        _categories = [NSSet setWithObject:[UNNotificationCategory categoryWithIdentifier:@"foreign" actions:@[] intentIdentifiers:@[] options:0]];
        _authorization = UNAuthorizationStatusAuthorized;
    }
    return self;
}
- (void)getNotificationSettingsWithCompletionHandler:(void (^)(UNNotificationSettings *))completion {
    RTTestSettings *settings = [RTTestSettings new]; settings.authorizationStatus = self.authorization;
    completion((UNNotificationSettings *)settings);
}
- (void)requestAuthorizationWithOptions:(UNAuthorizationOptions)options completionHandler:(void (^)(BOOL, NSError *))completion {
    (void)options; self.authorizationRequests++;
    if (self.holdAuthorization) self.authorizationCompletion = completion;
    else completion(NO, nil);
}
- (void)getNotificationCategoriesWithCompletionHandler:(void (^)(NSSet<UNNotificationCategory *> *))completion { completion(self.categories); }
- (void)setNotificationCategories:(NSSet<UNNotificationCategory *> *)categories { self.categories = categories; }
- (void)addNotificationRequest:(UNNotificationRequest *)request withCompletionHandler:(void (^)(NSError *))completion {
    [self.requests addObject:request]; [self.completions addObject:[completion copy]];
}
- (void)removePendingNotificationRequestsWithIdentifiers:(NSArray<NSString *> *)identifiers { [self.removed addObjectsFromArray:identifiers]; }
- (void)removeDeliveredNotificationsWithIdentifiers:(NSArray<NSString *> *)identifiers { [self.removed addObjectsFromArray:identifiers]; }
- (void)getDeliveredNotificationsWithCompletionHandler:(void (^)(NSArray<UNNotification *> *))completion { completion(@[]); }
- (void)finishOne {
    void (^completion)(NSError *) = self.completions.firstObject;
    [self.completions removeObjectAtIndex:0]; completion(nil);
}
- (void)finishAuthorization:(BOOL)granted {
    self.authorization = granted ? UNAuthorizationStatusAuthorized : UNAuthorizationStatusDenied;
    void (^completion)(BOOL, NSError *) = self.authorizationCompletion;
    self.authorizationCompletion = nil; completion(granted, nil);
}
@end

static void WaitFor(BOOL (^condition)(void)) {
    NSTimeInterval deadline = NSProcessInfo.processInfo.systemUptime + 5;
    while (!condition() && NSProcessInfo.processInfo.systemUptime < deadline)
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.005, false);
    assert(condition());
}
static RTNotificationClient *Client(RTTestCenter *center) {
    RTNotificationBroker *broker = [RTNotificationBroker new]; broker.center = (UNUserNotificationCenter *)center;
    center.delegate = broker;
    RTNotificationClient *client = [RTNotificationClient new]; client.broker = broker;
    client.ready = YES; client.capabilities = RTCapabilities;
    [broker.clients addObject:client]; return client;
}
static NSDictionary *Show(NSString *token, NSString *replaces, long long sequence) {
    return @{@"op": @"show", @"token": token, @"replaces": replaces, @"sequence": @(sequence),
        @"title": @"<literal> 100%", @"body": @" ", @"buttons": @[@"Yes", @"No"], @"sound": @"silent"};
}
static BOOL Completed(RTNotificationClient *client, long long sequence, BOOL success) {
    for (NSDictionary *event in client.events)
        if ([event[@"kind"] isEqual:@"operation"] && RTNumber(event[@"sequence"]) == sequence && [event[@"success"] boolValue] == success) return YES;
    return NO;
}

static void TestReplacementAndStaleActivation(void) {
    RTTestCenter *center = [RTTestCenter new]; RTNotificationClient *client = Client(center);
    NSString *first = @"11111111111111111111111111111111", *second = @"22222222222222222222222222222222";
    [client command:Show(first, @"", 1)];
    WaitFor(^BOOL { return center.completions.count == 1; });
    UNNotificationRequest *oldRequest = center.requests[0];
    assert([oldRequest.content.title isEqualToString:@"<literal> 100%"]);
    assert(oldRequest.content.sound == nil);
    assert(center.authorizationRequests == 0);
    [center finishOne]; WaitFor(^BOOL { return Completed(client, 1, YES); });
    [client command:Show(second, first, 2)];
    WaitFor(^BOOL { return center.completions.count == 1; });
    assert([center.requests[1].identifier isEqualToString:oldRequest.identifier]);
    assert(client.entries[first] == nil);
    [center finishOne]; WaitFor(^BOOL { return Completed(client, 2, YES); });
    RTTestResponse *response = [RTTestResponse new]; response.notification = [RTTestNotification new];
    response.notification.request = oldRequest; response.actionIdentifier = UNNotificationDefaultActionIdentifier;
    __block BOOL done = NO;
    [client.broker userNotificationCenter:(UNUserNotificationCenter *)center didReceiveNotificationResponse:(UNNotificationResponse *)response
        withCompletionHandler:^{ done = YES; }];
    WaitFor(^BOOL { return done; }); assert(client.entries[second] != nil);
    response.notification.request = center.requests[1]; response.actionIdentifier = client.entries[second].category.actions[1].identifier;
    done = NO;
    [client.broker userNotificationCenter:(UNUserNotificationCenter *)center didReceiveNotificationResponse:(UNNotificationResponse *)response
        withCompletionHandler:^{ done = YES; }];
    WaitFor(^BOOL { return done; }); assert(client.entries[second] == nil);
    BOOL activated = NO;
    for (NSDictionary *event in client.events)
        if ([event[@"kind"] isEqual:@"activated"] && [event[@"token"] isEqual:second] && RTNumber(event[@"button"]) == 2) activated = YES;
    assert(activated);
    [client stop];
    WaitFor(^BOOL { return center.delegate == nil; });
    assert(center.categories.count == 1 && [center.categories.anyObject.identifier isEqualToString:@"foreign"]);
}

static void TestStopOwnsLateDelivery(void) {
    RTTestCenter *center = [RTTestCenter new]; RTNotificationClient *client = Client(center);
    [client command:Show(@"33333333333333333333333333333333", @"", 3)];
    WaitFor(^BOOL { return center.completions.count == 1; });
    NSString *identifier = center.requests[0].identifier;
    [client command:@{@"op": @"stop", @"sequence": @4}];
    assert(!Completed(client, 4, YES));
    assert(client.entries.count == 0 && client.capabilities == 0);
    [center finishOne];
    WaitFor(^BOOL { return Completed(client, 4, YES); });
    assert(Completed(client, 3, NO));
    assert([center.removed containsObject:identifier]);
}

static void TestAuthorizationDenialAndBounds(void) {
    RTTestCenter *center = [RTTestCenter new]; center.authorization = UNAuthorizationStatusNotDetermined;
    RTNotificationClient *client = Client(center);
    assert(center.authorizationRequests == 0);
    [client command:Show(@"44444444444444444444444444444444", @"", 5)];
    WaitFor(^BOOL { return Completed(client, 5, NO); });
    assert(center.authorizationRequests == 1 && center.requests.count == 0 && client.entries.count == 0);
    assert(client.capabilities == 0);
    assert(!RTToken(@"../../path") && !RTIconName(@"file:///tmp/image") && !RTIconName(@"../image"));
    assert(RTIconName(@"com.example.application") && RTIconName(@"text-editor"));
    [client command:Show(@"invalid", @"", 6)]; assert(Completed(client, 6, NO));
    assert(!RTValidPng([NSData dataWithBytes:"not png" length:7]));
    unsigned char png[24] = {137, 80, 78, 71, 13, 10, 26, 10}; png[19] = 1; png[23] = 1;
    assert(RTValidPng([NSData dataWithBytes:png length:24]));
    png[16] = 127; assert(!RTValidPng([NSData dataWithBytes:png length:24]));
    client.stopped = YES; // Avoid scans while checking the C serialization boundary.
    size_t count = 0;
    void *bytes = rt_notifications_poll((__bridge void *)client, &count);
    assert(bytes && count > 0 && count < 256 * 1024);
    NSDictionary *state = [NSJSONSerialization JSONObjectWithData:[NSData dataWithBytes:bytes length:count] options:0 error:NULL];
    assert([state[@"ready"] boolValue] && RTArray(state[@"events"]).count > 0);
    rt_notifications_free(bytes);
    assert(rt_notifications_poll((__bridge void *)client, &count) == NULL);
    assert(rt_notifications_command((__bridge void *)client, "{}", 9 * 1024 * 1024) == 0);
    assert(rt_notifications_command((__bridge void *)client, "[]", 2) == 0);
    [client stop];
}

static void TestOverlappingReplacementUsesDistinctNativeId(void) {
    RTTestCenter *center = [RTTestCenter new]; RTNotificationClient *client = Client(center);
    NSString *first = @"55555555555555555555555555555555", *second = @"66666666666666666666666666666666";
    [client command:Show(first, @"", 10)];
    WaitFor(^BOOL { return center.completions.count == 1; });
    [client command:Show(second, first, 11)];
    WaitFor(^BOOL { return center.completions.count == 2; });
    assert(![center.requests[0].identifier isEqualToString:center.requests[1].identifier]);
    [center finishOne]; WaitFor(^BOOL { return Completed(client, 10, NO); });
    assert(![center.removed containsObject:center.requests[1].identifier]);
    [center finishOne]; WaitFor(^BOOL { return Completed(client, 11, YES); });
    [client stop];
}

static void TestDismissalPollingAndDelegateHandoff(void) {
    RTTestCenter *center = [RTTestCenter new]; RTNotificationClient *client = Client(center);
    NSString *token = @"77777777777777777777777777777777";
    [client command:Show(token, @"", 12)];
    WaitFor(^BOOL { return center.completions.count == 1; });
    [center finishOne]; WaitFor(^BOOL { return Completed(client, 12, YES); });
    RTNotificationEntry *entry = client.entries[token]; entry.deliveredAt -= 5;
    client.scanQueued = YES; [client scan]; WaitFor(^BOOL { return !client.scanQueued; });
    assert(client.entries[token] != nil);
    client.scanQueued = YES; [client scan]; WaitFor(^BOOL { return !client.scanQueued; });
    assert(client.entries[token] == nil);
    // A host taking over the delegate is not overwritten during cleanup.
    RTNotificationBroker *foreign = [RTNotificationBroker new]; center.delegate = foreign;
    [client scan]; assert(client.capabilities == 0);
    [client stop];
    __block BOOL flushed = NO; dispatch_async(dispatch_get_main_queue(), ^{ flushed = YES; });
    WaitFor(^BOOL { return flushed; }); assert(center.delegate == foreign);
}

static void TestReleasedHandleRetainsOnlyNativeLateCleanup(void) {
    RTTestCenter *center = [RTTestCenter new];
    __weak RTNotificationClient *weakClient;
    @autoreleasepool {
        RTNotificationClient *client = Client(center);
        weakClient = client;
        [client command:Show(@"88888888888888888888888888888888", @"", 13)];
        WaitFor(^BOOL { return center.completions.count == 1; });
        void *handle = (__bridge_retained void *)client;
        rt_notifications_destroy(handle);
    }
    WaitFor(^BOOL { return weakClient.stopped; });
    assert(weakClient != nil); // The pending OS completion still owns cleanup.
    [center finishOne];
    WaitFor(^BOOL { return weakClient == nil; });
    assert(center.removed.count >= 2);
}

static void TestCommandBufferIsCopiedAndPollBufferIsOwned(void) {
    RTTestCenter *center = [RTTestCenter new]; RTNotificationClient *client = Client(center);
    void *handle = (__bridge_retained void *)client;
    char command[] = "{\"op\":\"stop\",\"sequence\":42}";
    assert(rt_notifications_command(handle, command, strlen(command)) == 1);
    memset(command, 'x', strlen(command)); // Native async work must not borrow this buffer.
    WaitFor(^BOOL { return Completed(client, 42, YES); });
    size_t count = 0; void *bytes = rt_notifications_poll(handle, &count);
    assert(bytes && count > 0);
    rt_notifications_destroy(handle);
    NSDictionary *state = [NSJSONSerialization JSONObjectWithData:[NSData dataWithBytes:bytes length:count] options:0 error:NULL];
    assert([state[@"ready"] boolValue]);
    rt_notifications_free(bytes);
}

static void TestCancelledPaneCannotReappearAfterSharedAuthorization(void) {
    RTTestCenter *center = [RTTestCenter new]; center.authorization = UNAuthorizationStatusNotDetermined; center.holdAuthorization = YES;
    RTNotificationClient *client = Client(center);
    NSString *first = @"99999999999999999999999999999999", *second = @"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [client command:Show(first, @"", 20)];
    WaitFor(^BOOL { return center.authorizationRequests == 1; });
    RTNotificationEntry *cancelled = client.entries[first];
    [client command:@{@"op": @"close", @"token": first, @"sequence": @21}];
    assert(cancelled.command.count == 0 && client.entries[first] == nil);
    [client command:Show(second, @"", 22)];
    WaitFor(^BOOL { return client.entries[second].awaitingAuthorization; });
    assert(center.authorizationRequests == 1);
    [center finishAuthorization:YES];
    WaitFor(^BOOL { return center.completions.count == 1; });
    assert(center.requests.count == 1 && [center.requests[0].content.userInfo[@"royalToken"] isEqual:second]);
    [center finishOne]; WaitFor(^BOOL { return Completed(client, 22, YES); });
    [client stop];
}

static void TestLateImageSubmissionOwnsAttachmentCleanup(void) {
    RTTestCenter *center = [RTTestCenter new]; RTNotificationClient *client = Client(center);
    NSString *token = @"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    NSMutableDictionary *command = [Show(token, @"", 23) mutableCopy];
    command[@"image"] = @"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aDV8AAAAASUVORK5CYII=";
    [client command:command];
    WaitFor(^BOOL { return center.completions.count == 1; });
    RTNotificationEntry *entry = client.entries[token]; NSURL *directory = entry.directory;
    assert(directory && [NSFileManager.defaultManager fileExistsAtPath:directory.path]);
    [client command:@{@"op": @"close", @"token": token, @"sequence": @24}];
    assert(entry.submitting && entry.directory); // The in-flight OS add owns it.
    [center finishOne]; WaitFor(^BOOL { return Completed(client, 23, NO); });
    assert(entry.directory == nil && entry.command.count == 0);
    assert(![NSFileManager.defaultManager fileExistsAtPath:directory.path]);
    [client stop];
}

int main(void) {
    @autoreleasepool {
        TestReplacementAndStaleActivation();
        TestStopOwnsLateDelivery();
        TestAuthorizationDenialAndBounds();
        TestOverlappingReplacementUsesDistinctNativeId();
        TestDismissalPollingAndDelegateHandoff();
        TestReleasedHandleRetainsOnlyNativeLateCleanup();
        TestCommandBufferIsCopiedAndPollBufferIsOwned();
        TestCancelledPaneCannotReappearAfterSharedAuthorization();
        TestLateImageSubmissionOwnsAttachmentCleanup();
    }
    return 0;
}
