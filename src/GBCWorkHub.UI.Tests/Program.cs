using System;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.Tests
{
    /// <summary>
    /// 테스트 프레임워크 없이 실행 가능한 클립보드 TFS 동기화 프로토콜 스모크 테스트
    /// (GBCWorkHub.BIZ.Tests와 동일한 하우스 스타일).
    /// 전부 ClipboardProtocolLeaseManager + in-memory FakeClipboardAccessor로 결정론적으로 실행된다 —
    /// 실제 System.Windows.Clipboard, Thread.Sleep, 실제 타이머를 전혀 쓰지 않는다.
    /// </summary>
    public static class Program
    {
        private static int _failed;
        private static int _passed;

        private const string AckPrefix = TfsClipboardAckService.AckPrefix;
        private const string SyncPrefix = TfsClipboardAckService.SyncRequestPrefix;
        private const string TfsPayloadPrefix = "GBCWORKHUB_TFS::";

        public static int Main()
        {
            Test1_AckHold_RestoresUserText();
            Test2_AckHold_UserCopiedNewText_KeepsIt();
            Test3_SyncRequest_UserCopiedNewText_RetryDoesNotOverwrite();
            Test4_SyncRequest_SameRequestRetry_NoRedundantSetText();
            Test5_SyncRequest_IncomingTfsPayload_NotClobbered();
            Test6_AckLease1ThenLease2_Lease1DoesNotClobberLease2();
            Test7_CancelTimeoutException_OnlyRestoresIfExactMatch();
            Test8_EmptyBackup_OnlyClearsOwnValue_UserNewValueKept();
            Test9_LegacyPrefixesUnchanged_BackwardCompatible();
            Test10_CmcRetryLoop_UserCopyMidLoop_NeverOverwritten();
            Test11_ExplicitRetry_DiscardsInboundTfsThenWritesSync();
            Test12_CredentialsPrepare_RestoresPasswordFromOutboundToken();
            Test13_CredentialsPrepare_KeepsUserPassword();
            Test14_CredentialsPrepare_KeepsPasswordCopiedOverToken();

            Console.WriteLine();
            Console.WriteLine("Passed={0} Failed={1}", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static bool IsOwnProtocolNoise(string text)
        {
            return !string.IsNullOrEmpty(text) && text.StartsWith("GBCWORKHUB", StringComparison.Ordinal);
        }

        private static bool IsBlockingForeignPayload(string text)
        {
            return !string.IsNullOrEmpty(text) && text.StartsWith(TfsPayloadPrefix, StringComparison.Ordinal);
        }

        private static ClipboardProtocolLeaseManager NewManager(FakeClipboardAccessor fake)
        {
            return new ClipboardProtocolLeaseManager(fake, IsOwnProtocolNoise, msg => { });
        }

        // 1) 일반 텍스트 A -> WorkHub가 ACK 기록 -> 그대로 유지 -> hold 후 A 복원
        private static void Test1_AckHold_RestoresUserText()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);

            var write = mgr.WriteTransientAck("d1", AckPrefix + "d1");
            Assert("T1 write outcome", ClipboardWriteOutcome.Written, write.Outcome);
            Assert("T1 clipboard shows ACK", AckPrefix + "d1", fake.Text);

            bool acted = mgr.CompleteAckLease(write.Lease);
            Assert("T1 hold acted", true, acted);
            Assert("T1 restored to A", "A", fake.Text);
        }

        // 2) 일반 텍스트 A -> ACK 기록 -> 사용자가 B 복사 -> 기대: B 유지, A로 복원하거나 Clear하지 않음
        private static void Test2_AckHold_UserCopiedNewText_KeepsIt()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);

            var write = mgr.WriteTransientAck("d1", AckPrefix + "d1");
            fake.Text = "B"; // 사용자가 hold 도중 복사

            bool acted = mgr.CompleteAckLease(write.Lease);
            Assert("T2 hold did not act", false, acted);
            Assert("T2 clipboard still B", "B", fake.Text);
        }

        // 3) 일반 텍스트 A -> SYNC_REQUEST 기록 -> 사용자가 B 복사 -> retry tick -> B 유지
        private static void Test3_SyncRequest_UserCopiedNewText_RetryDoesNotOverwrite()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);
            string text = SyncPrefix + "{\"requestId\":\"r1\"}";

            var begin = mgr.BeginSyncRequest("r1", text, IsBlockingForeignPayload);
            Assert("T3 begin written", ClipboardWriteOutcome.Written, begin.Outcome);

            fake.Text = "B";
            var refresh = mgr.TryRefreshSyncRequest("r1", text, IsBlockingForeignPayload);
            Assert("T3 refresh displaced", ClipboardWriteOutcome.Displaced, refresh.Outcome);
            Assert("T3 clipboard still B", "B", fake.Text);
        }

        // 4) SYNC_REQUEST 기록 -> 동일 request retry -> 기대: 불필요한 SetText 없음
        private static void Test4_SyncRequest_SameRequestRetry_NoRedundantSetText()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);
            string text = SyncPrefix + "{\"requestId\":\"r1\"}";

            mgr.BeginSyncRequest("r1", text, IsBlockingForeignPayload);
            int setCountAfterBegin = fake.SetTextCallCount;

            var refresh = mgr.TryRefreshSyncRequest("r1", text, IsBlockingForeignPayload);
            Assert("T4 refresh already current", ClipboardWriteOutcome.AlreadyCurrent, refresh.Outcome);
            Assert("T4 no redundant SetText", setCountAfterBegin, fake.SetTextCallCount);
        }

        // 5) SYNC_REQUEST 대기 중 TFS Payload 도착 -> 기대: Payload를 복원/삭제로 훼손하지 않음
        private static void Test5_SyncRequest_IncomingTfsPayload_NotClobbered()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);
            string text = SyncPrefix + "{\"requestId\":\"r1\"}";

            mgr.BeginSyncRequest("r1", text, IsBlockingForeignPayload);
            fake.Text = TfsPayloadPrefix + "{\"success\":true}";

            var refresh = mgr.TryRefreshSyncRequest("r1", text, IsBlockingForeignPayload);
            Assert("T5 refresh skipped foreign payload",
                ClipboardWriteOutcome.SkippedForeignInboundPresent, refresh.Outcome);
            Assert("T5 payload untouched", TfsPayloadPrefix + "{\"success\":true}", fake.Text);

            // 마무리 경로도 미수집 TFS 응답을 건드리면 안 된다.
            bool ended = mgr.TryEndActiveSyncRequestLease();
            Assert("T5 end did not act (inbound TFS kept)", false, ended);
            Assert("T5 payload still untouched after end", TfsPayloadPrefix + "{\"success\":true}", fake.Text);

            bool remembered = mgr.TryRestoreRememberedBackupIfEmptyOrProtocol();
            Assert("T5 remembered restore skipped inbound TFS", false, remembered);
            Assert("T5 payload still untouched after remembered restore",
                TfsPayloadPrefix + "{\"success\":true}", fake.Text);
        }

        // 6) ACK lease 1 후 ACK lease 2 시작 -> lease 1의 지연 복원이 lease 2를 덮지 않음
        private static void Test6_AckLease1ThenLease2_Lease1DoesNotClobberLease2()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);

            var lease1 = mgr.WriteTransientAck("d1", AckPrefix + "d1").Lease;
            // lease1의 hold가 끝나기 전에 새 ACK(lease2) 발행 (예: 연속된 TFS 알림)
            var lease2 = mgr.WriteTransientAck("d2", AckPrefix + "d2").Lease;
            Assert("T6 clipboard shows lease2", AckPrefix + "d2", fake.Text);

            bool acted1 = mgr.CompleteAckLease(lease1);
            Assert("T6 lease1 completion no-op", false, acted1);
            Assert("T6 lease2 untouched by lease1", AckPrefix + "d2", fake.Text);

            bool acted2 = mgr.CompleteAckLease(lease2);
            Assert("T6 lease2 completion acted", true, acted2);
            Assert("T6 restored original A (backup carried through chain)", "A", fake.Text);
        }

        // 7) 취소/timeout/exception -> prefix면 복원/정리, 없으면 유지
        private static void Test7_CancelTimeoutException_OnlyRestoresIfExactMatch()
        {
            // 7a: 우리 프로토콜이 남아 있음 -> 복원
            var fakeA = new FakeClipboardAccessor { Text = "A" };
            var mgrA = NewManager(fakeA);
            string text = SyncPrefix + "{\"requestId\":\"r1\"}";
            mgrA.BeginSyncRequest("r1", text, IsBlockingForeignPayload);
            bool endedExact = mgrA.TryEndActiveSyncRequestLease();
            Assert("T7a end prefix acted", true, endedExact);
            Assert("T7a restored A", "A", fakeA.Text);

            // 7b: 다른 GBCWORKHUB* 잔여물도 prefix이므로 정리(복원)
            var fakeB = new FakeClipboardAccessor { Text = "A" };
            var mgrB = NewManager(fakeB);
            mgrB.BeginSyncRequest("r1", text, IsBlockingForeignPayload);
            fakeB.Text = "GBCWORKHUB_ACK::other-session";
            bool endedForeign = mgrB.TryEndActiveSyncRequestLease();
            Assert("T7b end acted on leftover prefix", true, endedForeign);
            Assert("T7b restored A over leftover protocol", "A", fakeB.Text);

            // 7c: 사용자가 일반 텍스트를 복사 -> 유지
            var fakeC = new FakeClipboardAccessor { Text = "A" };
            var mgrC = NewManager(fakeC);
            mgrC.BeginSyncRequest("r1", text, IsBlockingForeignPayload);
            fakeC.Text = "user pasted this";
            bool endedUser = mgrC.TryEndActiveSyncRequestLease();
            Assert("T7c end did not act on user text", false, endedUser);
            Assert("T7c user text kept", "user pasted this", fakeC.Text);

            // 7d: 클립보드가 비었으면 백업 복원 (RDP 종료 후 비움)
            var fakeD = new FakeClipboardAccessor { Text = "A" };
            var mgrD = NewManager(fakeD);
            mgrD.BeginSyncRequest("r1", text, IsBlockingForeignPayload);
            fakeD.Text = null;
            bool endedEmpty = mgrD.TryEndActiveSyncRequestLease();
            Assert("T7d end restored empty clipboard", true, endedEmpty);
            Assert("T7d restored A after empty", "A", fakeD.Text);
        }

        // 8) 백업이 비어 있던 경우 -> 기대: 자신이 쓴 값일 때만 Clear, 사용자 새 값은 유지
        private static void Test8_EmptyBackup_OnlyClearsOwnValue_UserNewValueKept()
        {
            // 8a: 클립보드가 비어있었고, hold 종료 시점에도 여전히 자기 값 -> Clear
            var fakeA = new FakeClipboardAccessor { Text = null };
            var mgrA = NewManager(fakeA);
            var write = mgrA.WriteTransientAck("d1", AckPrefix + "d1");
            Assert("T8a wrote ack with no backup", AckPrefix + "d1", fakeA.Text);
            bool acted = mgrA.CompleteAckLease(write.Lease);
            Assert("T8a cleared (no backup)", true, acted);
            Assert("T8a clipboard empty", null, fakeA.Text);
            Assert("T8a Clear called", 1, fakeA.ClearCallCount);

            // 8b: 클립보드가 비어있었지만, hold 도중 사용자가 새 값을 넣음 -> 유지, Clear 안 함
            var fakeB = new FakeClipboardAccessor { Text = null };
            var mgrB = NewManager(fakeB);
            var write2 = mgrB.WriteTransientAck("d2", AckPrefix + "d2");
            fakeB.Text = "userValue";
            bool acted2 = mgrB.CompleteAckLease(write2.Lease);
            Assert("T8b did not act", false, acted2);
            Assert("T8b user value kept", "userValue", fakeB.Text);
            Assert("T8b Clear not called", 0, fakeB.ClearCallCount);
        }

        // 9) 구형 Agent 형식의 ACK/Payload -> 기대: 기존과 동일하게 파싱·처리 (wire format 불변)
        private static void Test9_LegacyPrefixesUnchanged_BackwardCompatible()
        {
            Assert("T9 ack prefix unchanged", "GBCWORKHUB_ACK::", TfsClipboardAckService.AckPrefix);
            Assert("T9 sync prefix unchanged", "GBCWORKHUB_TFS_SYNC_REQUEST::", TfsClipboardAckService.SyncRequestPrefix);
            Assert("T9 IsProtocolText true for ack", true, TfsClipboardAckService.IsProtocolText("GBCWORKHUB_ACK::x"));
            Assert("T9 IsProtocolText true for tfs payload", true, TfsClipboardAckService.IsProtocolText("GBCWORKHUB_TFS::{}"));
            Assert("T9 IsProtocolText false for user text", false, TfsClipboardAckService.IsProtocolText("hello world"));
            // 해시는 원문을 노출하지 않는 형태(64자리 hex)로만 계산되어야 한다.
            string hash = TfsClipboardAckService.ComputeClipboardHash("some user text");
            Assert("T9 hash length", 64, hash.Length);
        }

        // 10) CMC 2초 retry 중 사용자 복사 -> 기대: 이후 retry가 사용자 값을 덮지 않음
        private static void Test10_CmcRetryLoop_UserCopyMidLoop_NeverOverwritten()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);
            string text = SyncPrefix + "{\"requestId\":\"cmc1\"}";

            mgr.BeginSyncRequest("cmc1", text, IsBlockingForeignPayload);

            // 처음 몇 번의 retry tick: 아무도 손대지 않았으니 계속 AlreadyCurrent
            for (int i = 0; i < 3; i++)
            {
                var r = mgr.TryRefreshSyncRequest("cmc1", text, IsBlockingForeignPayload);
                Assert("T10 tick " + i + " already current", ClipboardWriteOutcome.AlreadyCurrent, r.Outcome);
            }

            // 사용자가 루프 도중 복사
            fake.Text = "userMidLoop";

            // 이후 모든 retry tick은 displaced 상태로 절대 덮지 않는다
            for (int i = 0; i < 5; i++)
            {
                var r = mgr.TryRefreshSyncRequest("cmc1", text, IsBlockingForeignPayload);
                Assert("T10 post-copy tick " + i + " displaced", ClipboardWriteOutcome.Displaced, r.Outcome);
                Assert("T10 post-copy tick " + i + " clipboard untouched", "userMidLoop", fake.Text);
            }
        }

        // 11) 사용자 다시 시도: 남은 TFS payload를 치운 뒤 새 SYNC_REQUEST 기록
        private static void Test11_ExplicitRetry_DiscardsInboundTfsThenWritesSync()
        {
            var fake = new FakeClipboardAccessor { Text = "A" };
            var mgr = NewManager(fake);
            string syncText = SyncPrefix + "{\"requestId\":\"r2\"}";

            mgr.BeginSyncRequest("r1", SyncPrefix + "{\"requestId\":\"r1\"}", IsBlockingForeignPayload);
            fake.Text = TfsPayloadPrefix + "{\"success\":true}";

            bool discarded = mgr.TryDiscardInboundPayloadForRetry();
            Assert("T11 discarded inbound", true, discarded);
            Assert("T11 restored user backup A", "A", fake.Text);

            var begin = mgr.BeginSyncRequest("r2", syncText, IsBlockingForeignPayload);
            Assert("T11 new SYNC written", ClipboardWriteOutcome.Written, begin.Outcome);
            Assert("T11 clipboard is new SYNC", syncText, fake.Text);
        }

        // 12) 비밀번호를 먼저 복사 → TOKEN이 올라감 → 로그인 전 정리 시 비밀번호 복원
        private static void Test12_CredentialsPrepare_RestoresPasswordFromOutboundToken()
        {
            var fake = new FakeClipboardAccessor { Text = "secret-password" };
            var mgr = NewManager(fake);
            string token = TfsClipboardAckService.SessionTokenAnnouncePrefix + "{\"sessionToken\":\"abc\"}";

            var write = mgr.WriteTransientAck("abc", token);
            Assert("T12 token written", ClipboardWriteOutcome.Written, write.Outcome);
            Assert("T12 clipboard is token", token, fake.Text);

            bool acted = mgr.TryReleaseClipboardForUserCredentials();
            Assert("T12 restored", true, acted);
            Assert("T12 password back", "secret-password", fake.Text);
        }

        // 13) 클립보드가 이미 비밀번호면 그대로 둔다
        private static void Test13_CredentialsPrepare_KeepsUserPassword()
        {
            var fake = new FakeClipboardAccessor { Text = "secret-password" };
            var mgr = NewManager(fake);

            bool acted = mgr.TryReleaseClipboardForUserCredentials();
            Assert("T13 did not rewrite", false, acted);
            Assert("T13 password kept", "secret-password", fake.Text);
        }

        // 14) TOKEN 올린 뒤 사용자가 비밀번호를 복사 → 정리해도 비밀번호 유지 (덮지 않음)
        private static void Test14_CredentialsPrepare_KeepsPasswordCopiedOverToken()
        {
            var fake = new FakeClipboardAccessor { Text = "old" };
            var mgr = NewManager(fake);
            string token = TfsClipboardAckService.SessionTokenAnnouncePrefix + "{\"sessionToken\":\"abc\"}";
            mgr.WriteTransientAck("abc", token);
            fake.Text = "secret-password";

            bool acted = mgr.TryReleaseClipboardForUserCredentials();
            Assert("T14 did not overwrite password", false, acted);
            Assert("T14 password kept", "secret-password", fake.Text);
        }

        private static void Assert(string name, object expected, object actual)
        {
            bool ok = Equals(expected, actual);
            if (ok)
            {
                _passed++;
            }
            else
            {
                _failed++;
                Console.WriteLine("FAIL {0}: expected=[{1}] actual=[{2}]", name, expected, actual);
            }
        }
    }
}
