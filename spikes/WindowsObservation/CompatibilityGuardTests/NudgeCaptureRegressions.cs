using System.Text.Json;
using OfframpShift.Core;

namespace WindowsObservation;

internal static class NudgeCaptureRegressions
{
    private sealed class Queries(Guid personal, Guid work, Guid other) : IWindowMembershipQueries
    {
        private readonly Guid savedPersonal = personal;
        internal readonly List<string> Calls = [];
        internal Guid Identity = personal;
        internal Guid ForegroundIdentity = personal;
        internal int CurrentCode, IdentityCode, ForegroundCode;
        internal bool OnCurrent = true, MetadataThrows;
        internal nint InitialForeground = 1, FinalForeground = 1;
        internal SessionState InitialSession = SessionState.Active, FinalSession = SessionState.Active;
        internal Guid AfterCurrent = personal;
        internal uint AfterShell = 1;
        internal bool TopologyChanged;
        private int reads, identities, foregrounds, sessions;
        public nint Foreground { get { Calls.Add("foreground"); return foregrounds++ == 0 ? InitialForeground : FinalForeground; } }
        public SessionState Session { get { Calls.Add("session"); return sessions++ == 0 ? InitialSession : FinalSession; } }
        public DesktopReading Read()
        {
            Calls.Add("desktop");
            return reads++ == 0 ? new(Capability.Available, savedPersonal, [savedPersonal, work, other], 1) :
                new(Capability.Available, AfterCurrent, TopologyChanged ? [savedPersonal, work] : [savedPersonal, work, other], AfterShell);
        }
        public int IsOnCurrent(nint window, out bool current) { Calls.Add("membership"); current = OnCurrent; return CurrentCode; }
        public int GetIdentity(nint window, out Guid identity)
        {
            Calls.Add("identity"); var candidate = identities++ == 0;
            identity = candidate ? Identity : ForegroundIdentity; return candidate ? IdentityCode : ForegroundCode;
        }
        public bool Visible(nint window) { Calls.Add("visible"); return true; }
        public bool Valid(nint window) => MetadataThrows ? throw new InvalidOperationException() : true;
        public bool TopLevel(nint window) => true;
    }

    internal static void Run()
    {
        static void Check(bool value) { if (!value) throw new InvalidOperationException("Capture regression failed"); }
        var personal = Guid.NewGuid(); var work = Guid.NewGuid(); var other = Guid.NewGuid(); var absent = Guid.NewGuid();
        foreach (var (id, category) in new[] { (personal, DesktopIdentityCategory.Personal), (work, DesktopIdentityCategory.Work),
            (other, DesktopIdentityCategory.OtherEnumerated), (absent, DesktopIdentityCategory.NotEnumerated), (Guid.Empty, DesktopIdentityCategory.Empty) })
        {
            var queries = new Queries(personal, work, other) { Identity = id };
            var plain = new Queries(personal, work, other) { Identity = id };
            var diagnostic = new NudgeDesktopDiagnostic();
            var result = WindowDesktopMembership.Check(queries, 1, 1, personal, false, diagnostic, work);
            Check(result == WindowDesktopMembership.Check(plain, 1, 1, personal, false));
            Check(queries.Calls.SequenceEqual(plain.Calls)); // No repeated membership or context queries.
            Check(diagnostic.Result == result && diagnostic.HostingIdentity == category);
            Check(diagnostic.ValidWindow == true && diagnostic.VisibleWindow == true && diagnostic.TopLevelWindow == true);
            Check(diagnostic.CurrentMembershipHResult == MembershipHResult.S_OK && diagnostic.HostingIdentityHResult == MembershipHResult.S_OK);
            if (id != personal)
            {
                Check(result == NudgeDesktopCheck.CandidateWrongDesktop && diagnostic.Stage == NudgeCheckStage.CandidateIdentity);
                Check(diagnostic.ForegroundAfter == DiagnosticCheck.NotEvaluated && diagnostic.SessionAfter == DiagnosticCheck.NotEvaluated &&
                    diagnostic.ShellStable == DiagnosticCheck.NotEvaluated && diagnostic.TopologyStable == DiagnosticCheck.NotEvaluated);
                Check(diagnostic.ForegroundIdentityCode is null && queries.Calls.Count(x => x == "identity") == 1);
            }
            else Check(diagnostic.Stage == NudgeCheckStage.Allowed && diagnostic.ShellStable == DiagnosticCheck.Passed);
            var json = JsonSerializer.Serialize(diagnostic);
            foreach (var raw in new[] { personal, work, other, absent }) Check(!json.Contains(raw.ToString(), StringComparison.OrdinalIgnoreCase));
            Check(!json.Contains("ShellProcess", StringComparison.Ordinal) && !json.Contains("ForegroundWindow", StringComparison.Ordinal));
        }
        foreach (var scenario in new[] { "current-failed", "identity-failed", "foreground-identity-failed", "foreground-wrong", "not-current", "initial-foreground", "initial-session",
            "final-foreground", "final-session", "shell", "desktop", "topology", "metadata" })
        {
            Queries Make()
            {
                var q = new Queries(personal, work, other);
                switch (scenario)
                {
                    case "current-failed": q.CurrentCode = unchecked((int)0x80070005); break;
                    case "identity-failed": q.IdentityCode = unchecked((int)0x8002802B); break;
                    case "foreground-identity-failed": q.ForegroundCode = -1; break;
                    case "foreground-wrong": q.ForegroundIdentity = work; break;
                    case "not-current": q.OnCurrent = false; break;
                    case "initial-foreground": q.InitialForeground = 9; break;
                    case "initial-session": q.InitialSession = SessionState.Unknown; break;
                    case "final-foreground": q.FinalForeground = 9; break;
                    case "final-session": q.FinalSession = SessionState.Locked; break;
                    case "shell": q.AfterShell = 2; break;
                    case "desktop": q.AfterCurrent = work; break;
                    case "topology": q.TopologyChanged = true; break;
                    case "metadata": q.MetadataThrows = true; break;
                }
                return q;
            }
            var plain = Make(); var captured = Make(); var diagnostic = new NudgeDesktopDiagnostic { Role = NudgeWindowRole.Reminder };
            var result = WindowDesktopMembership.Check(captured, 2, 1, personal, false, diagnostic, work);
            Check(result == WindowDesktopMembership.Check(plain, 2, 1, personal, false) && captured.Calls.SequenceEqual(plain.Calls));
            Check(diagnostic.Role == NudgeWindowRole.Reminder);
            if (scenario == "current-failed") Check(diagnostic.CurrentMembershipHResult == MembershipHResult.AccessDenied && diagnostic.OnCurrent is null && diagnostic.HostingIdentityCode is null);
            if (scenario == "identity-failed") Check(diagnostic.HostingIdentity == DesktopIdentityCategory.Unavailable && diagnostic.HostingIdentityHResult == MembershipHResult.ElementNotFound);
            if (scenario == "initial-foreground") Check(diagnostic.SessionBefore == DiagnosticCheck.NotEvaluated && diagnostic.CurrentMembershipCode is null);
            if (scenario == "initial-session") Check(diagnostic.SessionBefore == DiagnosticCheck.Unavailable && diagnostic.PersonalBefore == DiagnosticCheck.NotEvaluated);
            if (scenario == "topology") Check(result == NudgeDesktopCheck.Allowed && diagnostic.TopologyStable == DiagnosticCheck.Failed);
            if (scenario == "metadata") Check(result == NudgeDesktopCheck.Allowed && diagnostic.ValidWindow is null);
        }
        Check(NudgeDesktopDiagnostic.Categorize(null, personal, personal, work, [personal, work]) == DesktopIdentityCategory.Unavailable);
        Check(NudgeDesktopDiagnostic.Categorize(0, null, personal, work, [personal, work]) == DesktopIdentityCategory.Unavailable);
        Check(NudgeDesktopDiagnostic.CategorizeHResult(unchecked((int)0x80070057)) == MembershipHResult.InvalidArgument);
        Check(NudgeDesktopDiagnostic.CategorizeHResult(1) == MembershipHResult.OtherSuccess);
        Console.WriteLine("PASS: captured authorization categories, early NotEvaluated checks, explicit roles, privacy and unchanged results/query counts (fake queries; no COM).");
    }
}
