namespace WindowsObservation;

public enum DesktopIdentityCategory { Personal, Work, OtherEnumerated, NotEnumerated, Empty, Unavailable }
public enum DiagnosticCheck { NotEvaluated, Passed, Failed, Unavailable }
public enum NudgeWindowRole { OriginalForeground, Reminder }
public enum NudgeCheckStage
{
    NotEvaluated, Unsupported, WorkerUnavailable, ForegroundBefore, SessionBefore, DesktopBefore,
    CurrentMembership, CandidateIdentity, ForegroundIdentity, DesktopAfter, ForegroundAfter, SessionAfter, Allowed
}
public enum MembershipHResult { NotEvaluated, S_OK, OtherSuccess, ElementNotFound, AccessDenied, InvalidArgument, OtherFailure }

// Fixed categories, scalar HRESULTs and booleans only. No native identifiers or arbitrary text.
public sealed record NudgeDesktopDiagnostic
{
    public NudgeWindowRole Role { get; init; } = NudgeWindowRole.OriginalForeground;
    public NudgeDesktopCheck Result { get; set; } = NudgeDesktopCheck.WorkerUnavailable;
    public NudgeCheckStage Stage { get; set; }
    public DesktopIdentityCategory CurrentBefore { get; set; } = DesktopIdentityCategory.Unavailable;
    public DesktopIdentityCategory CurrentAfter { get; set; } = DesktopIdentityCategory.Unavailable;
    public int? CurrentMembershipCode { get; set; }
    public MembershipHResult CurrentMembershipHResult => CategorizeHResult(CurrentMembershipCode);
    public bool? OnCurrent { get; set; }
    public int? HostingIdentityCode { get; set; }
    public MembershipHResult HostingIdentityHResult => CategorizeHResult(HostingIdentityCode);
    public DesktopIdentityCategory HostingIdentity { get; set; } = DesktopIdentityCategory.Unavailable;
    public int? ForegroundIdentityCode { get; set; }
    public DesktopIdentityCategory ForegroundIdentity { get; set; } = DesktopIdentityCategory.Unavailable;
    public bool? ValidWindow { get; set; }
    public bool? VisibleWindow { get; set; }
    public bool? TopLevelWindow { get; set; }
    public DiagnosticCheck ForegroundBefore { get; set; }
    public DiagnosticCheck SessionBefore { get; set; }
    public DiagnosticCheck PersonalBefore { get; set; }
    public DiagnosticCheck PersonalAfter { get; set; }
    public DiagnosticCheck ShellStable { get; set; }
    public DiagnosticCheck TopologyStable { get; set; }
    public DiagnosticCheck ForegroundAfter { get; set; }
    public DiagnosticCheck SessionAfter { get; set; }

    public static MembershipHResult CategorizeHResult(int? code) => code switch
    {
        null => MembershipHResult.NotEvaluated,
        0 => MembershipHResult.S_OK,
        >= 0 => MembershipHResult.OtherSuccess,
        unchecked((int)0x8002802B) => MembershipHResult.ElementNotFound,
        unchecked((int)0x80070005) => MembershipHResult.AccessDenied,
        unchecked((int)0x80070057) => MembershipHResult.InvalidArgument,
        _ => MembershipHResult.OtherFailure
    };

    public static DesktopIdentityCategory Categorize(int? code, Guid? identity, Guid personal, Guid work, Guid[] desktops)
    {
        if (code is null or < 0 || identity is null) return DesktopIdentityCategory.Unavailable;
        if (identity == Guid.Empty) return DesktopIdentityCategory.Empty;
        if (identity == personal) return DesktopIdentityCategory.Personal;
        if (identity == work) return DesktopIdentityCategory.Work;
        return desktops.Contains(identity.Value) ? DesktopIdentityCategory.OtherEnumerated : DesktopIdentityCategory.NotEnumerated;
    }
}

public sealed record NudgeCheckResponse(NudgeDesktopCheck Result, NudgeDesktopDiagnostic? Diagnostic);
