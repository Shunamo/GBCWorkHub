using System;
using System.Collections.Generic;
using System.Linq;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ.Tests
{
    /// <summary>
    /// 테스트 프레임워크 없이 실행 가능한 WorkLog 파서 스모크 테스트.
    /// </summary>
    public static class Program
    {
        private static int _failed;
        private static int _passed;

        public static int Main()
        {
            var parser = new TfsWorkLogParser();
            var session = new WorkSessionContext
            {
                RemoteIp = "10.230.35.245",
                CurrentUserName = "hubuser",
                CurrentUserId = "DOMAIN\\hubuser",
                SessionStartedAt = new DateTime(2026, 7, 22, 9, 0, 0),
                SessionEndedAt = new DateTime(2026, 7, 22, 18, 0, 0)
            };

            Test_XamlCs_ClientUi(parser, session);
            Test_DeployClientUiDll(parser, session);
            Test_DeployServerDtoDll(parser, session);
            Test_EqsPath(parser, session);
            Test_DbPackage(parser, session);
            Test_DbView(parser, session);
            Test_DbSqlUnresolved(parser, session);
            Test_GlobalResource(parser, session);
            Test_ProjectName(parser, session);
            Test_DedupeSamePath(parser, session);
            Test_SameFileNameDifferentPath(parser, session);
            Test_UnclassifiedPreserved(parser, session);
            Test_TicketTn(parser, session);
            Test_NoFalseTicket(parser, session);
            Test_CandidateRecommendAndExclude(parser, session);
            Test_SamplePreviewDump(parser, session);

            Console.WriteLine();
            Console.WriteLine("Passed={0} Failed={1}", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Test_XamlCs_ClientUi(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 1, "fix",
                "$/HISSolutions/HIS.MC.DR.RM/UI/HIS.MC.DR.RM.RS.UI/SelectFoo.xaml.cs",
                "SelectFoo.xaml.cs");
            var g = FirstGroup(draft);
            Assert("1 .xaml.cs Type", "Client", g.Type);
            Assert("1 .xaml.cs Category", "UI", g.Category);
            Assert("1 .xaml.cs Rule", "TFS_CLIENT_UI", g.AppliedRuleCode);
        }

        private static void Test_DeployClientUiDll(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 2, "edit",
                "$/HISSolutions/HIS.Deploy/Client/MC/HIS.MC.NM.IN.PM.UI.dll",
                "HIS.MC.NM.IN.PM.UI.dll");
            var g = FirstGroup(draft);
            Assert("2 Deploy UI.dll Type", "Client", g.Type);
            Assert("2 Deploy UI.dll Category", "UI", g.Category);
            Assert("2 Deploy UI.dll Project", "HIS.MC.NM.IN.PM.UI", g.ProjectName);
        }

        private static void Test_DeployServerDtoDll(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 3, "edit",
                "$/HISSolutions/HIS.Deploy/Server/MC/HIS.MC.FOO.DTO.dll",
                "HIS.MC.FOO.DTO.dll");
            var g = FirstGroup(draft);
            Assert("3 Server DTO Type", "Server", g.Type);
            Assert("3 Server DTO Category", "DTO", g.Category);
        }

        private static void Test_EqsPath(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 4, "add",
                "$/HISSolutions/EQS/SomeFolder/Sample.eqs",
                "Sample.eqs");
            var g = FirstGroup(draft);
            Assert("4 EQS Type", "EQS", g.Type);
            Assert("4 EQS Category", "EQS", g.Category);
        }

        private static void Test_DbPackage(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 5, "edit",
                "$/DB/Oracle/PackageBody/xcom.PKG_TAHALUF.sql",
                "packagebody xcom.PKG_TAHALUF.sql");
            var g = FirstGroup(draft);
            Assert("5 Package Type", "DB Object", g.Type);
            Assert("5 Package Category", "Package", g.Category);
        }

        private static void Test_DbView(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 6, "edit",
                "$/DB/Oracle/View/xsup.MSDMDBAV.sql",
                "view xsup.MSDMDBAV.sql");
            var g = FirstGroup(draft);
            Assert("6 View Type", "DB Object", g.Type);
            Assert("6 View Category", "View", g.Category);
        }

        private static void Test_DbSqlUnresolved(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 7, "edit",
                "$/DB/Oracle/Scripts/misc_update.sql",
                "misc_update.sql");
            var g = FirstGroup(draft);
            Assert("7 SQL Type", "DB Object", g.Type);
            Assert("7 SQL Category empty", string.Empty, g.Category ?? string.Empty);
            AssertTrue("7 SQL NeedsReview", g.NeedsReview);
        }

        private static void Test_GlobalResource(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 8, "edit",
                "$/HISSolutions/HIS.Deploy/Client/GlobalResource/MS.2057.Short.Dictionary.resources.xml",
                "MS.2057.Short.Dictionary.resources.xml");
            var g = FirstGroup(draft);
            Assert("8 GlobalRes Category", "Global Resource", g.Category);
            Assert("8 GlobalRes Type", "Client", g.Type);
        }

        private static void Test_ProjectName(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 9, "edit",
                "$/HISSolutions/HIS.MS.CS.PH.MD/UI/HIS.MS.CS.PH.MD.UI/Foo.xaml",
                "Foo.xaml");
            var g = FirstGroup(draft);
            Assert("9 ProjectName", "HIS.MS.CS.PH.MD.UI", g.ProjectName);
        }

        private static void Test_DedupeSamePath(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var cs = new TfsChangesetItem
            {
                ChangesetId = 10,
                Comment = "TN-1",
                Files = new List<TfsChangedFileItem>
                {
                    File("$/A/B/C.xaml", "C.xaml", "edit"),
                    File("$/A/B/C.xaml", "C.xaml", "edit")
                }
            };
            var draft = parser.Parse(cs, session);
            int count = draft.WorkGroups.Sum(g => g.SourceItems.Count);
            AssertTrue("10 dedupe count==1", count == 1);
        }

        private static void Test_SameFileNameDifferentPath(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var cs = new TfsChangesetItem
            {
                ChangesetId = 11,
                Comment = "TN-2",
                Files = new List<TfsChangedFileItem>
                {
                    File("$/Client/A/Same.xaml", "Same.xaml", "edit"),
                    File("$/Client/B/Same.xaml", "Same.xaml", "edit")
                }
            };
            var draft = parser.Parse(cs, session);
            int count = draft.WorkGroups.Sum(g => g.SourceItems.Count);
            AssertTrue("11 same name different path count==2", count == 2);
        }

        private static void Test_UnclassifiedPreserved(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            // 비후보(.txt)는 업무기록 Source에서 제외
            var draft = ParseOne(parser, session, 12, "edit",
                "$/HISSolutions/Docs/readme.txt",
                "readme.txt");
            AssertTrue("12 not-candidate skipped groups==0", draft.WorkGroups == null || draft.WorkGroups.Count == 0);
            AssertTrue("12 not-candidate in SkippedItems",
                draft.SkippedItems != null && draft.SkippedItems.Exists(s => s != null && s.StartsWith("CAND_NOT_CANDIDATE", StringComparison.Ordinal)));
        }

        private static void Test_CandidateRecommendAndExclude(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            // 직접 분류기
            AssertTrue("15a .cs recommended",
                TfsWorkLogCandidateClassifier.Classify("Foo.cs", "$/A/Foo.cs").Decision
                == TfsWorkLogCandidateClassifier.Decision.Recommended);
            AssertTrue("15b .xaml recommended",
                TfsWorkLogCandidateClassifier.Classify("Foo.xaml", "$/A/Foo.xaml").Decision
                == TfsWorkLogCandidateClassifier.Decision.Recommended);
            AssertTrue("15c .dll recommended",
                TfsWorkLogCandidateClassifier.Classify("HIS.A.UI.dll", "$/Deploy/HIS.A.UI.dll").Decision
                == TfsWorkLogCandidateClassifier.Decision.Recommended);
            AssertTrue("15d .sql recommended",
                TfsWorkLogCandidateClassifier.Classify("a.sql", "$/DB/a.sql").Decision
                == TfsWorkLogCandidateClassifier.Decision.Recommended);
            AssertTrue("15e .eqs recommended",
                TfsWorkLogCandidateClassifier.Classify("a.eqs", "$/EQS/a.eqs").Decision
                == TfsWorkLogCandidateClassifier.Decision.Recommended);
            AssertTrue("15f resource recommended",
                TfsWorkLogCandidateClassifier.Classify(
                    "MS.2057.Short.Dictionary.resources.xml",
                    "$/HIS.Deploy/Client/GlobalResource/MS.2057.Short.Dictionary.resources.xml").Decision
                == TfsWorkLogCandidateClassifier.Decision.Recommended);

            AssertTrue("15g .g.cs excluded",
                TfsWorkLogCandidateClassifier.Classify("View.g.cs", "$/UI/obj/Debug/View.g.cs").Decision
                == TfsWorkLogCandidateClassifier.Decision.Excluded);
            AssertTrue("15h Designer.cs excluded",
                TfsWorkLogCandidateClassifier.Classify("Form1.Designer.cs", "$/UI/Form1.Designer.cs").Decision
                == TfsWorkLogCandidateClassifier.Decision.Excluded);
            AssertTrue("15i .csproj excluded",
                TfsWorkLogCandidateClassifier.Classify("HIS.A.UI.csproj", "$/UI/HIS.A.UI.csproj").Decision
                == TfsWorkLogCandidateClassifier.Decision.Excluded);
            AssertTrue("15j .tmp excluded",
                TfsWorkLogCandidateClassifier.Classify("x.tmp", "$/A/x.tmp").Decision
                == TfsWorkLogCandidateClassifier.Decision.Excluded);
            AssertTrue("15k .txt not candidate",
                TfsWorkLogCandidateClassifier.Classify("notes.txt", "$/Docs/notes.txt").Decision
                == TfsWorkLogCandidateClassifier.Decision.NotCandidate);
            AssertTrue("15n .disco excluded",
                TfsWorkLogCandidateClassifier.Classify("Service.disco", "$/WebRefs/Service.disco").Decision
                == TfsWorkLogCandidateClassifier.Decision.Excluded);
            AssertTrue("15o .wsdl excluded",
                TfsWorkLogCandidateClassifier.Classify("Service.wsdl", "$/WebRefs/Service.wsdl").Decision
                == TfsWorkLogCandidateClassifier.Decision.Excluded);

            // 파서: 추천만 Source로, 제외/비후보는 SkippedItems
            var cs = new TfsChangesetItem
            {
                ChangesetId = 15,
                Comment = "TN-15",
                Files = new List<TfsChangedFileItem>
                {
                    File("$/UI/Foo.xaml", "Foo.xaml", "edit"),
                    File("$/UI/obj/Debug/Foo.g.cs", "Foo.g.cs", "edit"),
                    File("$/UI/HIS.A.UI.csproj", "HIS.A.UI.csproj", "edit"),
                    File("$/Docs/notes.txt", "notes.txt", "edit")
                }
            };
            var draft = parser.Parse(cs, session);
            int sourceCount = draft.WorkGroups.Sum(g => g.SourceItems.Count);
            AssertTrue("15l parser keeps only recommended", sourceCount == 1);
            AssertTrue("15m skipped has exclude+notcand", draft.SkippedItems.Count >= 3);
        }

        private static void Test_TicketTn(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 13, "edit",
                "$/A/UI/B.xaml", "B.xaml",
                "2026-07-22, jeongdaeun, TN-12022(APO) : 팝업 수정");
            Assert("13 TicketNo", "12022", draft.TicketNo);
            Assert("13 PC", "10.230.35.245", draft.Pc);
            Assert("13 Person empty", string.Empty, draft.PersonInCharge ?? string.Empty);

            var bracket = ParseOne(parser, session, 131, "edit",
                "$/A/UI/B.xaml", "B.xaml",
                "2026-07-22, jeongdaeun, [1234] : 브라켓 티켓");
            Assert("13b bracket TicketNo", "1234", bracket.TicketNo);

            var mixed = ParseOne(parser, session, 132, "edit",
                "$/A/UI/B.xaml", "B.xaml",
                "[1234] TN-5678 수정");
            Assert("13c mixed TicketNo", "1234, 5678", mixed.TicketNo);

            var withCs = ParseOne(parser, session, 133, "edit",
                "$/A/UI/B.xaml", "B.xaml",
                "[CS 99881] TN-12022 : CS는 티켓 아님");
            Assert("13d ignore CS marker TicketNo", "12022", withCs.TicketNo);
        }

        private static void Test_NoFalseTicket(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var draft = ParseOne(parser, session, 14, "edit",
                "$/A/UI/B.xaml", "B.xaml",
                "2026-07-22, jeongdaeun, changeset 998877 : 일반 수정");
            Assert("14 no false TicketNo", string.Empty, draft.TicketNo ?? string.Empty);

            var csOnly = ParseOne(parser, session, 141, "edit",
                "$/A/UI/B.xaml", "B.xaml",
                "[CS 1234] 체인지셋만 있는 코멘트");
            Assert("14b CS marker not ticket", string.Empty, csOnly.TicketNo ?? string.Empty);
        }

        private static void Test_SamplePreviewDump(ITfsWorkLogParser parser, WorkSessionContext session)
        {
            var cs = new TfsChangesetItem
            {
                ChangesetId = 99881,
                AuthorId = "jeongdaeun",
                AuthorName = "정대운",
                CheckedInAt = "2026-07-22T15:30:00",
                Comment = "2026-07-22, jeongdaeun, TN-12022(APO) : 팝업 수정",
                Files = new List<TfsChangedFileItem>
                {
                    File("$/HISSolutions/HIS.MC.DR.RM/UI/HIS.MC.DR.RM.RS.UI/SelectMedicalTreatmentRecord.xaml.cs",
                        "SelectMedicalTreatmentRecord.xaml.cs", "edit"),
                    File("$/HISSolutions/HIS.Deploy/Client/MC/HIS.MC.NM.IN.PM.UI.dll",
                        "HIS.MC.NM.IN.PM.UI.dll", "edit"),
                    File("$/HISSolutions/HIS.Deploy/Client/GlobalResource/MS.2057.Short.Dictionary.resources.xml",
                        "MS.2057.Short.Dictionary.resources.xml", "edit"),
                    File("$/HISSolutions/HIS.Deploy/Client/GlobalResource/MS.2057.Short.Dictionary.resources.dll",
                        "MS.2057.Short.Dictionary.resources.dll", "edit"),
                    File("$/DB/Oracle/PackageBody/xcom.PKG_TAHALUF.sql",
                        "packagebody xcom.PKG_TAHALUF.sql", "edit"),
                    File("$/HISSolutions/Docs/notes.txt", "notes.txt", "edit")
                }
            };

            var draft = parser.Parse(cs, session);
            Console.WriteLine("===== SAMPLE PREVIEW =====");
            Console.WriteLine(WorkLogExcelPreviewMapper.FormatDebugPreview(draft));
            var rows = WorkLogExcelPreviewMapper.ToPreviewRows(draft);
            Console.WriteLine("Excel Preview Rows: " + rows.Count);
            foreach (var r in rows)
            {
                Console.WriteLine("  Row Type={0} Category={1} Project={2} Sources={3}",
                    r.Type, r.Category, r.ProjectName,
                    (r.SourcePathAndFileName ?? "").Replace(Environment.NewLine, " | "));
            }
            AssertTrue("sample TicketNo", draft.TicketNo == "12022");
            AssertTrue("sample has groups", draft.WorkGroups.Count >= 3);
        }

        private static WorkLogDraft ParseOne(
            ITfsWorkLogParser parser,
            WorkSessionContext session,
            int id,
            string changeType,
            string path,
            string fileName,
            string comment = "TN-1 test")
        {
            return parser.Parse(new TfsChangesetItem
            {
                ChangesetId = id,
                Comment = comment,
                Files = new List<TfsChangedFileItem> { File(path, fileName, changeType) }
            }, session);
        }

        private static TfsChangedFileItem File(string path, string name, string changeType)
        {
            return new TfsChangedFileItem
            {
                ServerPath = path,
                FileName = name,
                ChangeType = changeType
            };
        }

        private static WorkGroupDraft FirstGroup(WorkLogDraft draft)
        {
            if (draft.WorkGroups == null || draft.WorkGroups.Count == 0)
                throw new InvalidOperationException("No work groups");
            return draft.WorkGroups[0];
        }

        private static void Assert(string name, string expected, string actual)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " expected=[" + expected + "] actual=[" + actual + "]");
            }
        }

        private static void AssertTrue(string name, bool condition)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name);
            }
        }
    }
}
