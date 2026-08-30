using System.IO;
using BatchRenamer.Core;
using BatchRenamer.Transaction;
using BatchRenamer.FileSystem;

static RenameRuleSet Rules(string baseName, NameCaseMode mode) => new(
    BaseName: baseName,
    OriginalNameMode: OriginalNameMode.None,
    Prefix: string.Empty,
    Suffix: string.Empty,
    LiteralSearch: string.Empty,
    LiteralReplacement: string.Empty,
    CaseMode: mode,
    Sequence: new SequenceConfig(false, 1, 1, 3, BatchRenamer.Core.SequencePosition.AfterName, "_"));

static string Preview(string stem, string extension, string baseName, NameCaseMode mode)
{
    var input = new[]
    {
        new PreviewInputItem(Guid.NewGuid(), @"C:\Test", stem + extension, stem, extension, true),
    };
    return PreviewEngine.Build(input, Rules(baseName, mode)).Items[0].NewName;
}

static void Expect(string actual, string expected, string name)
{
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
    Console.WriteLine($"PASS  {name}: {actual}");
}

static void ExpectIssue(ValidationBatchResult result, Guid id, string code, bool expected, string name)
{
    var actual = result.Items.Single(x => x.ItemId == id).Issues.Any(x => x.Code == code);
    if (actual != expected)
        throw new InvalidOperationException($"{name}: issue {code}, expected={expected}, actual={actual}.");
    Console.WriteLine($"PASS  {name}: {code}={(actual ? "present" : "absent")}");
}

static void ExpectTrue(bool condition, string name, string? detail = null)
{
    if (!condition) throw new InvalidOperationException($"{name}: {detail ?? "condition was false"}.");
    Console.WriteLine($"PASS  {name}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}")}");
}

Expect(Preview("x", ".JPG", "HELLO WORLD", NameCaseMode.TitleCaseWords), "Hello World.JPG", "ALL CAPS words");
Expect(Preview("x", ".JPG", "HELLO_WORLD-test", NameCaseMode.TitleCaseWords), "Hello_World-Test.JPG", "filename separators");
Expect(Preview("x", ".JPG", "my PHOTO collection", NameCaseMode.TitleCaseWords), "My Photo Collection.JPG", "mixed case normalization");
Expect(Preview("x", ".JPG", "2026 SUMMER-trip", NameCaseMode.TitleCaseWords), "2026 Summer-Trip.JPG", "digits and separators");
Expect(Preview("x", ".JPG", "AbC", NameCaseMode.Lower), "abc.JPG", "lower");
Expect(Preview("x", ".JPG", "AbC", NameCaseMode.Upper), "ABC.JPG", "upper");

// Find/replace scope is intentionally limited to the retained original stem.
var replaceId = Guid.NewGuid();
var replaceRules = new RenameRuleSet(
    BaseName: string.Empty,
    OriginalNameMode: OriginalNameMode.AfterBaseName,
    Prefix: string.Empty,
    Suffix: string.Empty,
    LiteralSearch: "IMG_",
    LiteralReplacement: "Photo_",
    CaseMode: NameCaseMode.Unchanged,
    Sequence: new SequenceConfig(false, 1, 1, 3, BatchRenamer.Core.SequencePosition.AfterName, "_"));
var replacePreview = PreviewEngine.Build(
    [new PreviewInputItem(replaceId, @"C:\Test", "IMG_001.JPG", "IMG_001", ".JPG", true)],
    replaceRules);
Expect(replacePreview.Items[0].NewName, "Photo_001.JPG", "find/replace retained original");

var ignoredReplaceId = Guid.NewGuid();
var ignoredReplaceRules = replaceRules with
{
    BaseName = "Fixed",
    OriginalNameMode = OriginalNameMode.None,
};
var ignoredReplacePreview = PreviewEngine.Build(
    [new PreviewInputItem(ignoredReplaceId, @"C:\Test", "IMG_001.JPG", "IMG_001", ".JPG", true)],
    ignoredReplaceRules);
Expect(ignoredReplacePreview.Items[0].NewName, "Fixed.JPG", "find/replace ignored when original not retained");

var fs = new FakeFileSystem();
var sem = new FakeSemanticsProvider(caseSensitive: false);
var identities = new FakeIdentityProvider();

// Invalid / reserved filename.
var invalidId = Guid.NewGuid();
var invalid = ValidationEngine.Validate(
    [new ValidationInputItem(invalidId, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "CON?.txt", false, true, false, null)],
    fs, sem, identities);
ExpectIssue(invalid, invalidId, "INVALID_CHARACTER", true, "invalid character");
ExpectIssue(invalid, invalidId, "RESERVED_NAME", false, "reserved name not confused by invalid candidate");

var reservedId = Guid.NewGuid();
var reserved = ValidationEngine.Validate(
    [new ValidationInputItem(reservedId, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "CON.txt", false, true, false, null)],
    fs, sem, identities);
ExpectIssue(reserved, reservedId, "RESERVED_NAME", true, "Windows reserved name");

// Full Windows invalid-character matrix. '&' is intentionally legal.
foreach (var ch in new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
{
    var id = Guid.NewGuid();
    var candidate = $"A{ch}B.txt";
    var result = ValidationEngine.Validate(
        [new ValidationInputItem(id, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", candidate, false, true, true, null)],
        fs, sem, identities);
    ExpectIssue(result, id, "INVALID_CHARACTER", true, $"invalid char {ch}");
}

var ampId = Guid.NewGuid();
var ampersand = ValidationEngine.Validate(
    [new ValidationInputItem(ampId, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "A&B.txt", false, true, true, null)],
    fs, sem, identities);
ExpectIssue(ampersand, ampId, "INVALID_CHARACTER", false, "ampersand is legal");

foreach (var name in new[] { "PRN.txt", "AUX.txt", "NUL.txt", "COM1.txt", "LPT9.txt" })
{
    var id = Guid.NewGuid();
    var result = ValidationEngine.Validate(
        [new ValidationInputItem(id, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", name, false, true, true, null)],
        fs, sem, identities);
    ExpectIssue(result, id, "RESERVED_NAME", true, $"reserved {name}");
}

var conSuffixId = Guid.NewGuid();
var conSuffix = ValidationEngine.Validate(
    [new ValidationInputItem(conSuffixId, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "CON_01.txt", false, true, true, null)],
    fs, sem, identities);
ExpectIssue(conSuffix, conSuffixId, "RESERVED_NAME", false, "CON_01 is legal");

// Duplicate target.
var d1 = Guid.NewGuid();
var d2 = Guid.NewGuid();
var duplicate = ValidationEngine.Validate(
    [
        new ValidationInputItem(d1, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "X.txt", false, true, true, null),
        new ValidationInputItem(d2, @"C:\Test\B.txt", @"C:\Test", "B.txt", ".txt", "x.TXT", false, true, true, null),
    ], fs, sem, identities);
ExpectIssue(duplicate, d1, "DUPLICATE_TARGET", true, "case-insensitive duplicate target A");
ExpectIssue(duplicate, d2, "DUPLICATE_TARGET", true, "case-insensitive duplicate target B");

// A <-> B exchange: both current targets exist but both are vacating, so TARGET_EXISTS must not fire.
fs.SetExisting(@"C:\Test\A.txt", @"C:\Test\B.txt");
var a = Guid.NewGuid();
var b = Guid.NewGuid();
var swap = ValidationEngine.Validate(
    [
        new ValidationInputItem(a, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "B.txt", false, true, false, null),
        new ValidationInputItem(b, @"C:\Test\B.txt", @"C:\Test", "B.txt", ".txt", "A.txt", false, true, false, null),
    ], fs, sem, identities);
ExpectIssue(swap, a, "TARGET_EXISTS", false, "A-B swap A");
ExpectIssue(swap, b, "TARGET_EXISTS", false, "A-B swap B");

// Existing target outside VacatingSourceSet must block.
fs.SetExisting(@"C:\Test\A.txt", @"C:\Test\External.txt");
var externalId = Guid.NewGuid();
var external = ValidationEngine.Validate(
    [new ValidationInputItem(externalId, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "External.txt", false, true, false, null)],
    fs, sem, identities);
ExpectIssue(external, externalId, "TARGET_EXISTS", true, "external target occupied");

// Unchecked source does not vacate its path.
fs.SetExisting(@"C:\Test\A.txt", @"C:\Test\B.txt");
var checkedId = Guid.NewGuid();
var uncheckedId = Guid.NewGuid();
var uncheckedOccupant = ValidationEngine.Validate(
    [
        new ValidationInputItem(checkedId, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "B.txt", false, true, false, null),
        new ValidationInputItem(uncheckedId, @"C:\Test\B.txt", @"C:\Test", "B.txt", ".txt", "B.txt", false, false, false, null),
    ], fs, sem, identities);
ExpectIssue(uncheckedOccupant, checkedId, "TARGET_EXISTS", true, "unchecked occupant blocks target");

// Case-only rename is self occupancy on a case-insensitive directory.
fs.SetExisting(@"C:\Test\photo.jpg");
var caseId = Guid.NewGuid();
var caseOnly = ValidationEngine.Validate(
    [new ValidationInputItem(caseId, @"C:\Test\photo.jpg", @"C:\Test", "photo.jpg", ".jpg", "Photo.jpg", false, true, false, null)],
    fs, sem, identities);
ExpectIssue(caseOnly, caseId, "TARGET_EXISTS", false, "case-only rename allowed");

// ---------------- V0.5 RenamePlanner ----------------
var plannerFs = new FakeFileSystem();
var plannerSem = new FakeSemanticsProvider(caseSensitive: false);
var plannerIds = new FakeIdentityProvider();
var sourceIdentity = new FileIdentity(0x12345678, 0x0000000000000042);
plannerFs.SetExisting(@"C:\Test\A.txt");
plannerIds.Set(@"C:\Test\A.txt", sourceIdentity);
var plannerItemId = Guid.NewGuid();
var fixedTransactionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
var fixedCreatedAt = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
var plannerResult = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(
        plannerItemId, @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "Photo_001.txt",
        false, true, false, sourceIdentity)],
    plannerFs, plannerSem, plannerIds, fixedTransactionId, fixedCreatedAt);
ExpectTrue(plannerResult.Success, "planner simple plan success");
ExpectTrue(plannerResult.Plan is not null, "planner produced plan");
var plan = plannerResult.Plan!;
ExpectTrue(plan.TransactionId == fixedTransactionId, "planner transaction id frozen");
ExpectTrue(plan.CreatedAt == fixedCreatedAt, "planner timestamp frozen");
ExpectTrue(plan.SchemaVersion == RenamePlanner.CurrentSchemaVersion, "planner schema version");
ExpectTrue(plan.Entries.Count == 1, "planner changed rows only");
ExpectTrue(plan.DirectorySemantics.Count == 1, "planner semantics snapshot");
var entry = plan.Entries[0];
ExpectTrue(entry.SourcePath.EndsWith(@"Test\A.txt", StringComparison.OrdinalIgnoreCase), "planner source path");
ExpectTrue(entry.TargetPath.EndsWith(@"Test\Photo_001.txt", StringComparison.OrdinalIgnoreCase), "planner target path");
ExpectTrue(entry.TemporaryPath.Contains(".~br-", StringComparison.Ordinal), "planner safe temp namespace");
ExpectTrue(!string.Equals(entry.SourcePath, entry.TemporaryPath, StringComparison.OrdinalIgnoreCase), "temp differs from source");
ExpectTrue(!string.Equals(entry.TargetPath, entry.TemporaryPath, StringComparison.OrdinalIgnoreCase), "temp differs from target");
ExpectTrue(entry.ExpectedFileIdentity == sourceIdentity, "planner freezes fresh file identity");

// Excluded and unchanged rows never enter the transaction plan.
plannerFs.SetExisting(@"C:\Test\A.txt", @"C:\Test\B.txt", @"C:\Test\C.txt");
var mixedResult = RenamePlanner.BuildFinalPlan(
    [
        new ValidationInputItem(Guid.NewGuid(), @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "X.txt", false, true, false, null),
        new ValidationInputItem(Guid.NewGuid(), @"C:\Test\B.txt", @"C:\Test", "B.txt", ".txt", "B.txt", false, true, false, null),
        new ValidationInputItem(Guid.NewGuid(), @"C:\Test\C.txt", @"C:\Test", "C.txt", ".txt", "Y.txt", false, false, false, null),
    ], plannerFs, plannerSem, plannerIds);
ExpectTrue(mixedResult.Success && mixedResult.Plan?.Entries.Count == 1, "planner skips unchanged/excluded");

// No changes is not an executable plan, but it is not a validation failure either.
plannerFs.SetExisting(@"C:\Test\A.txt");
var noChange = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(Guid.NewGuid(), @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "A.txt", false, true, false, null)],
    plannerFs, plannerSem, plannerIds);
ExpectTrue(!noChange.Success && noChange.Plan is null, "planner no-change has no plan");
ExpectTrue(noChange.PlannerIssues.Any(x => x.Code == "NO_CHANGES"), "planner no-change reason");

// Preview-only synthetic rows can never leak into a real plan.
var syntheticPlan = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(Guid.NewGuid(), @"C:\Synthetic\A.txt", @"C:\Synthetic", "A.txt", ".txt", "X.txt", false, true, true, null)],
    plannerFs, plannerSem, plannerIds);
ExpectTrue(!syntheticPlan.Success && syntheticPlan.PlannerIssues.Any(x => x.Code == "SYNTHETIC_ITEM_NOT_EXECUTABLE"),
    "planner blocks synthetic data");

// V1 extension protection is enforced again at the planner boundary even if a future generator misbehaves.
plannerFs.SetExisting(@"C:\Test\A.txt");
var extensionViolation = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(Guid.NewGuid(), @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "A.jpg", false, true, false, null)],
    plannerFs, plannerSem, plannerIds);
ExpectTrue(!extensionViolation.Success && extensionViolation.PlannerIssues.Any(x => x.Code == "V1_EXTENSION_LOCK_VIOLATION"),
    "planner extension lock");

// A <-> B remains valid at the final planner gate.
plannerFs.SetExisting(@"C:\Test\A.txt", @"C:\Test\B.txt");
var swapPlan = RenamePlanner.BuildFinalPlan(
    [
        new ValidationInputItem(Guid.NewGuid(), @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "B.txt", false, true, false, null),
        new ValidationInputItem(Guid.NewGuid(), @"C:\Test\B.txt", @"C:\Test", "B.txt", ".txt", "A.txt", false, true, false, null),
    ], plannerFs, plannerSem, plannerIds);
ExpectTrue(swapPlan.Success && swapPlan.Plan?.Entries.Count == 2, "planner A-B swap");
ExpectTrue(swapPlan.Plan!.Entries.Select(x => x.TemporaryPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
    "planner temp paths unique");

// Case-only rename is still a changed entry and must use a temporary namespace in V1.
plannerFs.SetExisting(@"C:\Test\photo.jpg");
var casePlanId = Guid.NewGuid();
var casePlan = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(casePlanId, @"C:\Test\photo.jpg", @"C:\Test", "photo.jpg", ".jpg", "Photo.jpg", false, true, false, null)],
    plannerFs, plannerSem, plannerIds);
ExpectTrue(casePlan.Success && casePlan.Plan?.Entries.Count == 1, "planner case-only rename");
var caseOnlyPlannerPlan = casePlan.Plan ?? throw new InvalidOperationException("planner case-only test expected a non-null plan");
ExpectTrue(!string.Equals(caseOnlyPlannerPlan.Entries[0].TemporaryPath, caseOnlyPlannerPlan.Entries[0].SourcePath, StringComparison.OrdinalIgnoreCase),
    "planner case-only uses temp");

// Source type replacement is caught by the final validation owned by RenamePlanner.
plannerFs.SetEntry(@"C:\Test\Folder", FileSystemEntryKind.File);
var kindChanged = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(Guid.NewGuid(), @"C:\Test\Folder", @"C:\Test", "Folder", string.Empty, "Folder2", true, true, false, null)],
    plannerFs, plannerSem, plannerIds);
ExpectIssue(kindChanged.FinalValidation, kindChanged.FinalValidation.Items[0].ItemId, "SOURCE_KIND_CHANGED", true, "planner final source-kind guard");
ExpectTrue(!kindChanged.Success, "planner blocks source-kind change");

// Source object replacement is also caught at the final planner gate.
var oldIdentity = new FileIdentity(0x10, 0x20);
var newIdentity = new FileIdentity(0x10, 0x21);
plannerFs.SetExisting(@"C:\Test\Identity.txt");
plannerIds.Set(@"C:\Test\Identity.txt", newIdentity);
var identityChangedId = Guid.NewGuid();
var identityChanged = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(identityChangedId, @"C:\Test\Identity.txt", @"C:\Test", "Identity.txt", ".txt", "Renamed.txt", false, true, false, oldIdentity)],
    plannerFs, plannerSem, plannerIds);
ExpectIssue(identityChanged.FinalValidation, identityChangedId, "SOURCE_IDENTITY_CHANGED", true, "planner final identity guard");
ExpectTrue(!identityChanged.Success, "planner blocks source replacement");

// If identity was known when imported but can no longer be read, safety must not silently downgrade
// the plan to a null identity. Final Validation rejects the uncertain source instead.
plannerFs.SetExisting(@"C:\Test\KnownIdentity.txt");
var identityUnverifiableId = Guid.NewGuid();
var identityUnverifiable = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(
        identityUnverifiableId, @"C:\Test\KnownIdentity.txt", @"C:\Test", "KnownIdentity.txt", ".txt", "KnownIdentity2.txt",
        false, true, false, new FileIdentity(0x44, 0x55))],
    plannerFs, plannerSem, plannerIds);
ExpectIssue(identityUnverifiable.FinalValidation, identityUnverifiableId, "SOURCE_IDENTITY_UNVERIFIABLE", true,
    "planner blocks identity verification downgrade");
ExpectTrue(!identityUnverifiable.Success, "planner rejects unverifiable known identity");

// Planner must never reuse an occupied temp namespace; failure is safer than overwrite.
plannerFs.SetExisting(@"C:\Test\A.txt");
plannerFs.BlockTemporaryNames = true;
var blockedTemp = RenamePlanner.BuildFinalPlan(
    [new ValidationInputItem(Guid.NewGuid(), @"C:\Test\A.txt", @"C:\Test", "A.txt", ".txt", "X.txt", false, true, false, null)],
    plannerFs, plannerSem, plannerIds);
ExpectTrue(!blockedTemp.Success && blockedTemp.PlannerIssues.Any(x => x.Code == "TEMP_NAME_ALLOCATION_FAILED"),
    "planner refuses occupied temp namespace");
plannerFs.BlockTemporaryNames = false;

// ---------------- V0.6-A RenamePlan Persistence ----------------
var transactionRoot = Path.Combine(Path.GetTempPath(), $"BatchRenamer-Smoke-{Guid.NewGuid():N}");
try
{
    var persisted = RenamePlanPersistence.PersistNew(plan, transactionRoot);
    ExpectTrue(persisted.Success, "plan persistence success");
    ExpectTrue(persisted.PersistedPlan is not null, "plan read-back success");
    ExpectTrue(File.Exists(persisted.PlanPath), "plan.json exists");
    ExpectTrue(!string.IsNullOrWhiteSpace(persisted.Sha256) && persisted.Sha256!.Length == 64, "plan.json SHA256");
    ExpectTrue(persisted.PersistedPlan!.TransactionId == plan.TransactionId, "persisted transaction id");
    ExpectTrue(persisted.PersistedPlan.Entries.Count == plan.Entries.Count, "persisted entry count");
    var persistedJson = File.ReadAllText(persisted.PlanPath!);
    ExpectTrue(!persistedJson.Contains("\"renameCount\"", StringComparison.Ordinal), "plan.json excludes computed RenameCount");
    ExpectTrue(!Directory.EnumerateFiles(persisted.TransactionDirectory!, ".plan.json.tmp-*", SearchOption.TopDirectoryOnly).Any(),
        "plan persistence staging cleanup");

    var loaded = RenamePlanPersistence.Load(persisted.PlanPath!);
    ExpectTrue(loaded.Success && loaded.Plan is not null, "explicit plan load");
    ExpectTrue(string.Equals(loaded.Sha256, persisted.Sha256, StringComparison.Ordinal), "plan load hash stable");

    var duplicatePersist = RenamePlanPersistence.PersistNew(plan, transactionRoot);
    ExpectTrue(!duplicatePersist.Success
               && duplicatePersist.Issues.Any(x => x.Code == "TRANSACTION_ALREADY_EXISTS"),
        "plan persistence never overwrites existing transaction");

    var wrongDirectory = Path.Combine(transactionRoot, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(wrongDirectory);
    var wrongDirectoryPlan = Path.Combine(wrongDirectory, RenamePlanPersistence.PlanFileName);
    File.Copy(persisted.PlanPath!, wrongDirectoryPlan);
    var mismatchedDirectoryLoad = RenamePlanPersistence.Load(wrongDirectoryPlan);
    ExpectTrue(!mismatchedDirectoryLoad.Success
               && mismatchedDirectoryLoad.Issues.Any(x => x.Code == "PLAN_TRANSACTION_DIRECTORY_MISMATCH"),
        "plan load binds transaction id to directory");

    var invalidSchema = plan with { SchemaVersion = 999 };
    var invalidIssues = RenamePlanIntegrity.Validate(invalidSchema);
    ExpectTrue(invalidIssues.Any(x => x.Code == "PLAN_SCHEMA_UNSUPPORTED"), "plan integrity rejects unknown schema");
}
finally
{
    try { if (Directory.Exists(transactionRoot)) Directory.Delete(transactionRoot, recursive: true); } catch { }
}

// ---------------- V0.6-B Transaction Preflight ----------------
plannerFs.SetExisting(@"C:\Test\A.txt");
plannerIds.Set(@"C:\Test\A.txt", sourceIdentity);
var preflightOk = TransactionPreflight.Validate(plan, plannerFs, plannerSem, plannerIds);
ExpectTrue(preflightOk.CanExecute, "preflight simple plan success");

plannerFs.SetExisting();
var preflightMissing = TransactionPreflight.Validate(plan, plannerFs, plannerSem, plannerIds);
ExpectTrue(!preflightMissing.CanExecute && preflightMissing.Issues.Any(x => x.Code == "SOURCE_MISSING"),
    "preflight source missing");

plannerFs.SetExisting(@"C:\Test\A.txt");
plannerIds.Set(@"C:\Test\A.txt", new FileIdentity(0x12345678, 0x99));
var preflightIdentityChanged = TransactionPreflight.Validate(plan, plannerFs, plannerSem, plannerIds);
ExpectTrue(!preflightIdentityChanged.CanExecute
           && preflightIdentityChanged.Issues.Any(x => x.Code == "SOURCE_IDENTITY_CHANGED"),
    "preflight identity changed");
plannerIds.Set(@"C:\Test\A.txt", sourceIdentity);

plannerFs.SetExisting(@"C:\Test\A.txt", @"C:\Test\Photo_001.txt");
var preflightTargetOccupied = TransactionPreflight.Validate(plan, plannerFs, plannerSem, plannerIds);
ExpectTrue(!preflightTargetOccupied.CanExecute
           && preflightTargetOccupied.Issues.Any(x => x.Code == "TARGET_EXISTS"),
    "preflight external target occupancy");

plannerFs.SetExisting(@"C:\Test\A.txt");
plannerFs.BlockTemporaryNames = true;
var preflightTempOccupied = TransactionPreflight.Validate(plan, plannerFs, plannerSem, plannerIds);
ExpectTrue(!preflightTempOccupied.CanExecute
           && preflightTempOccupied.Issues.Any(x => x.Code == "TEMP_ALREADY_EXISTS"),
    "preflight temp occupancy");
plannerFs.BlockTemporaryNames = false;

plannerFs.SetExisting(@"C:\Test\A.txt", @"C:\Test\B.txt");
var preflightSwap = TransactionPreflight.Validate(swapPlan.Plan!, plannerFs, plannerSem, plannerIds);
ExpectTrue(preflightSwap.CanExecute, "preflight A-B swap allows vacating targets");

plannerFs.SetExisting(@"C:\Test\A.txt");
var changedSemantics = new FakeSemanticsProvider(caseSensitive: true);
var preflightSemanticsChanged = TransactionPreflight.Validate(plan, plannerFs, changedSemantics, plannerIds);
ExpectTrue(!preflightSemanticsChanged.CanExecute
           && preflightSemanticsChanged.Issues.Any(x => x.Code == "PATH_SEMANTICS_CHANGED"),
    "preflight blocks changed path semantics");


// ---------------- V0.6-C Transaction Phase 1: Source -> Temp ----------------
// This is the first smoke suite that performs real namespace mutations. It uses only a dedicated
// disposable directory under %TEMP%; it never imports or touches user-selected files.
var phase1Root = Path.Combine(Path.GetTempPath(), $"BatchRenamer-Phase1-Smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(phase1Root);
try
{
    var realFs = new WindowsReadOnlyFileSystem();
    var realSemantics = new WindowsPathSemanticsProvider();
    var realIdentities = new WindowsFileIdentityProvider();
    var mutationFs = new SystemRenameMutationFileSystem();

    // Two real files verify the full successful Phase-1 prefix and FileIdentity continuity.
    var phase1A = Path.Combine(phase1Root, "A.txt");
    var phase1B = Path.Combine(phase1Root, "B.txt");
    File.WriteAllText(phase1A, "alpha");
    File.WriteAllText(phase1B, "beta");
    var identityA = realIdentities.TryGetIdentity(phase1A, isDirectory: false);
    var identityB = realIdentities.TryGetIdentity(phase1B, isDirectory: false);
    ExpectTrue(identityA is not null && identityB is not null, "phase1 Windows FileIdentity available");

    var phase1Sem = realSemantics.GetSemantics(phase1Root);
    var phase1Plan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        [new RenamePlanDirectorySemantics(
            phase1Root,
            phase1Sem.IsCaseSensitive,
            phase1Sem.IsReliable,
            phase1Sem.MaxComponentLength,
            phase1Sem.MaxPathLength,
            phase1Sem.Source)],
        [
            new RenamePlanEntry(0, Guid.NewGuid(), phase1A, Path.Combine(phase1Root, ".~br-phase1-a"), Path.Combine(phase1Root, "A2.txt"), false, identityA),
            new RenamePlanEntry(1, Guid.NewGuid(), phase1B, Path.Combine(phase1Root, ".~br-phase1-b"), Path.Combine(phase1Root, "B2.txt"), false, identityB),
        ]);

    var phase1 = TransactionPhase1Executor.Execute(
        phase1Plan, realFs, realSemantics, realIdentities, mutationFs);
    ExpectTrue(phase1.Success && phase1.State == Phase1ExecutionState.Completed, "phase1 real file batch completed");
    ExpectTrue(phase1.AppliedEntries.Count == 2, "phase1 applied entry count");
    ExpectTrue(!File.Exists(phase1A) && !File.Exists(phase1B), "phase1 sources vacated");
    ExpectTrue(File.Exists(phase1Plan.Entries[0].TemporaryPath) && File.Exists(phase1Plan.Entries[1].TemporaryPath), "phase1 temp entries exist");
    Expect(File.ReadAllText(phase1Plan.Entries[0].TemporaryPath), "alpha", "phase1 file content A preserved");
    Expect(File.ReadAllText(phase1Plan.Entries[1].TemporaryPath), "beta", "phase1 file content B preserved");
    ExpectTrue(realIdentities.TryGetIdentity(phase1Plan.Entries[0].TemporaryPath, false) == identityA, "phase1 identity A preserved");
    ExpectTrue(realIdentities.TryGetIdentity(phase1Plan.Entries[1].TemporaryPath, false) == identityB, "phase1 identity B preserved");
    ExpectTrue(!File.Exists(phase1Plan.Entries[0].TargetPath) && !File.Exists(phase1Plan.Entries[1].TargetPath), "phase1 never creates final targets");

    // Test-only cleanup. This is deliberately NOT production rollback and does not exercise a public
    // rollback engine; it merely restores the disposable smoke sandbox for subsequent cases.
    File.Move(phase1Plan.Entries[0].TemporaryPath, phase1A, overwrite: false);
    File.Move(phase1Plan.Entries[1].TemporaryPath, phase1B, overwrite: false);

    // A pre-existing temp path must block before any mutation.
    var occupiedTemp = Path.Combine(phase1Root, ".~br-phase1-occupied");
    File.WriteAllText(occupiedTemp, "external");
    var occupiedPlan = phase1Plan with
    {
        TransactionId = Guid.NewGuid(),
        Entries = [phase1Plan.Entries[0] with { TemporaryPath = occupiedTemp }],
    };
    var occupiedResult = TransactionPhase1Executor.Execute(
        occupiedPlan, realFs, realSemantics, realIdentities, mutationFs);
    ExpectTrue(occupiedResult.State == Phase1ExecutionState.NotStarted && !occupiedResult.HasMutation, "phase1 occupied temp blocks before mutation");
    ExpectTrue(occupiedResult.Issues.Any(x => x.Code == "TEMP_ALREADY_EXISTS"), "phase1 occupied temp issue");
    ExpectTrue(File.Exists(phase1A) && File.ReadAllText(phase1A) == "alpha", "phase1 occupied temp leaves source untouched");
    File.Delete(occupiedTemp);

    // Directory move support uses the same no-overwrite Phase-1 contract.
    var sourceDirectory = Path.Combine(phase1Root, "FolderA");
    Directory.CreateDirectory(sourceDirectory);
    File.WriteAllText(Path.Combine(sourceDirectory, "child.txt"), "child");
    var directoryIdentity = realIdentities.TryGetIdentity(sourceDirectory, isDirectory: true);
    ExpectTrue(directoryIdentity is not null, "phase1 directory FileIdentity available");
    var tempDirectory = Path.Combine(phase1Root, ".~br-phase1-dir");
    var directoryPlan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        phase1Plan.DirectorySemantics,
        [new RenamePlanEntry(0, Guid.NewGuid(), sourceDirectory, tempDirectory, Path.Combine(phase1Root, "FolderB"), true, directoryIdentity)]);
    var directoryPhase1 = TransactionPhase1Executor.Execute(
        directoryPlan, realFs, realSemantics, realIdentities, mutationFs);
    ExpectTrue(directoryPhase1.Success, "phase1 real directory completed");
    ExpectTrue(!Directory.Exists(sourceDirectory) && Directory.Exists(tempDirectory), "phase1 directory moved to temp");
    ExpectTrue(File.Exists(Path.Combine(tempDirectory, "child.txt")), "phase1 directory contents preserved");
    ExpectTrue(realIdentities.TryGetIdentity(tempDirectory, true) == directoryIdentity, "phase1 directory identity preserved");
    Directory.Move(tempDirectory, sourceDirectory); // test-only cleanup

    // Inject a real failure on the second mutation. The executor must report the exact applied prefix,
    // stop immediately, and never create any final target. Cleanup remains confined to this sandbox.
    var failureA = Path.Combine(phase1Root, "FailureA.txt");
    var failureB = Path.Combine(phase1Root, "FailureB.txt");
    File.WriteAllText(failureA, "one");
    File.WriteAllText(failureB, "two");
    var failureIdentityA = realIdentities.TryGetIdentity(failureA, false);
    var failureIdentityB = realIdentities.TryGetIdentity(failureB, false);
    ExpectTrue(failureIdentityA is not null && failureIdentityB is not null, "phase1 failure-case identities available");
    var failureTempA = Path.Combine(phase1Root, ".~br-phase1-failure-a");
    var failureTempB = Path.Combine(phase1Root, ".~br-phase1-failure-b");
    var failureTargetA = Path.Combine(phase1Root, "FailureA2.txt");
    var failureTargetB = Path.Combine(phase1Root, "FailureB2.txt");
    var failurePlan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        phase1Plan.DirectorySemantics,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), failureA, failureTempA, failureTargetA, false, failureIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), failureB, failureTempB, failureTargetB, false, failureIdentityB),
        ]);
    var injectedFailure = TransactionPhase1Executor.Execute(
        failurePlan,
        realFs,
        realSemantics,
        realIdentities,
        new FailOnNthMutationFileSystem(mutationFs, failOnCall: 2));
    ExpectTrue(injectedFailure.State == Phase1ExecutionState.FailedPartial && injectedFailure.RequiresRecovery, "phase1 partial failure state");
    ExpectTrue(injectedFailure.AppliedEntries.Count == 1 && injectedFailure.AppliedEntries[0].Ordinal == 0, "phase1 partial failure exact prefix");
    ExpectTrue(injectedFailure.Issues.Any(x => x.Code == "PHASE1_MOVE_FAILED"), "phase1 partial failure issue");
    ExpectTrue(!File.Exists(failureA) && File.Exists(failureTempA), "phase1 partial failure first entry at temp");
    ExpectTrue(File.Exists(failureB) && !File.Exists(failureTempB), "phase1 partial failure second entry untouched");
    ExpectTrue(!File.Exists(failureTargetA) && !File.Exists(failureTargetB), "phase1 partial failure never creates targets");
    File.Move(failureTempA, failureA, overwrite: false); // test-only cleanup

    // Simulate a provider that applies the move and then throws. The executor must reconcile the
    // namespace instead of falsely reporting zero mutation.
    var afterApplySource = Path.Combine(phase1Root, "AfterApply.txt");
    var afterApplyTemp = Path.Combine(phase1Root, ".~br-phase1-after-apply");
    var afterApplyTarget = Path.Combine(phase1Root, "AfterApply2.txt");
    File.WriteAllText(afterApplySource, "after-apply");
    var afterApplyIdentity = realIdentities.TryGetIdentity(afterApplySource, false);
    ExpectTrue(afterApplyIdentity is not null, "phase1 after-apply identity available");
    var afterApplyPlan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        phase1Plan.DirectorySemantics,
        [new RenamePlanEntry(0, Guid.NewGuid(), afterApplySource, afterApplyTemp, afterApplyTarget, false, afterApplyIdentity)]);
    var afterApplyResult = TransactionPhase1Executor.Execute(
        afterApplyPlan,
        realFs,
        realSemantics,
        realIdentities,
        new ApplyThenThrowOnNthMutationFileSystem(mutationFs, throwOnCall: 1));
    ExpectTrue(afterApplyResult.State == Phase1ExecutionState.FailedPartial && afterApplyResult.RequiresRecovery,
        "phase1 apply-then-throw recovery state");
    ExpectTrue(afterApplyResult.AppliedEntries.Count == 1, "phase1 apply-then-throw reconciles applied entry");
    ExpectTrue(afterApplyResult.Issues.Any(x => x.Code == "PHASE1_MOVE_EXCEPTION_AFTER_APPLY"),
        "phase1 apply-then-throw issue");
    ExpectTrue(!File.Exists(afterApplySource) && File.Exists(afterApplyTemp),
        "phase1 apply-then-throw observes temp state");
    File.Move(afterApplyTemp, afterApplySource, overwrite: false); // test-only cleanup
}
finally
{
    try { if (Directory.Exists(phase1Root)) Directory.Delete(phase1Root, recursive: true); } catch { }
}



// ---------------- V0.6-D Phase 2 + V0.6-E Rollback Foundation ----------------
// All mutations remain confined to a dedicated disposable %TEMP% sandbox. These tests verify the
// complete two-phase namespace protocol, cycle safety, case-only spelling, partial failure states,
// and filesystem-driven idempotent rollback. The normal application execute button remains unwired.
var phase2Root = Path.Combine(Path.GetTempPath(), $"BatchRenamer-Phase2-Rollback-Smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(phase2Root);
try
{
    var realFs = new WindowsReadOnlyFileSystem();
    var realSemantics = new WindowsPathSemanticsProvider();
    var realIdentities = new WindowsFileIdentityProvider();
    var mutationFs = new SystemRenameMutationFileSystem();
    var exactInspector = new SystemExactNamespaceInspector();
    var phase2Sem = realSemantics.GetSemantics(phase2Root);
    var phase2SemanticsSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(
            phase2Root,
            phase2Sem.IsCaseSensitive,
            phase2Sem.IsReliable,
            phase2Sem.MaxComponentLength,
            phase2Sem.MaxPathLength,
            phase2Sem.Source),
    };

    // Successful files: Source -> Temp -> Target, then rollback Target -> Temp -> Source.
    var finalA = Path.Combine(phase2Root, "FinalA.txt");
    var finalB = Path.Combine(phase2Root, "FinalB.txt");
    File.WriteAllText(finalA, "alpha-final");
    File.WriteAllText(finalB, "beta-final");
    var finalIdentityA = realIdentities.TryGetIdentity(finalA, false);
    var finalIdentityB = realIdentities.TryGetIdentity(finalB, false);
    ExpectTrue(finalIdentityA is not null && finalIdentityB is not null, "phase2 Windows FileIdentity available");
    var finalPlan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        phase2SemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), finalA, Path.Combine(phase2Root, ".~br-phase2-final-a"), Path.Combine(phase2Root, "RenamedA.txt"), false, finalIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), finalB, Path.Combine(phase2Root, ".~br-phase2-final-b"), Path.Combine(phase2Root, "RenamedB.txt"), false, finalIdentityB),
        ]);
    var finalPhase1 = TransactionPhase1Executor.Execute(finalPlan, realFs, realSemantics, realIdentities, mutationFs);
    ExpectTrue(finalPhase1.Success, "phase2 setup Phase1 completed");
    var finalPhase2 = TransactionPhase2Executor.Execute(finalPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(finalPhase2.Success && finalPhase2.State == Phase2ExecutionState.Completed, "phase2 real file batch completed");
    ExpectTrue(finalPhase2.AppliedEntries.Count == 2, "phase2 applied entry count");
    ExpectTrue(!File.Exists(finalPlan.Entries[0].TemporaryPath) && !File.Exists(finalPlan.Entries[1].TemporaryPath), "phase2 temps vacated");
    ExpectTrue(File.Exists(finalPlan.Entries[0].TargetPath) && File.Exists(finalPlan.Entries[1].TargetPath), "phase2 targets exist");
    Expect(File.ReadAllText(finalPlan.Entries[0].TargetPath), "alpha-final", "phase2 target A content preserved");
    Expect(File.ReadAllText(finalPlan.Entries[1].TargetPath), "beta-final", "phase2 target B content preserved");
    ExpectTrue(realIdentities.TryGetIdentity(finalPlan.Entries[0].TargetPath, false) == finalIdentityA, "phase2 target A identity preserved");
    ExpectTrue(realIdentities.TryGetIdentity(finalPlan.Entries[1].TargetPath, false) == finalIdentityB, "phase2 target B identity preserved");

    var completedRollback = TransactionRollbackExecutor.Execute(
        finalPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(completedRollback.Success, "rollback completed Phase2 restores sources");
    ExpectTrue(File.Exists(finalA) && File.Exists(finalB), "rollback sources restored");
    ExpectTrue(!File.Exists(finalPlan.Entries[0].TemporaryPath) && !File.Exists(finalPlan.Entries[1].TemporaryPath), "rollback leaves no temps");
    ExpectTrue(!File.Exists(finalPlan.Entries[0].TargetPath) && !File.Exists(finalPlan.Entries[1].TargetPath), "rollback vacates final targets");
    Expect(File.ReadAllText(finalA), "alpha-final", "rollback source A content restored");
    Expect(File.ReadAllText(finalB), "beta-final", "rollback source B content restored");
    ExpectTrue(realIdentities.TryGetIdentity(finalA, false) == finalIdentityA, "rollback source A identity restored");
    ExpectTrue(realIdentities.TryGetIdentity(finalB, false) == finalIdentityB, "rollback source B identity restored");
    var idempotentRollback = TransactionRollbackExecutor.Execute(
        finalPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(idempotentRollback.Success && !idempotentRollback.HasMutation, "rollback idempotent second run");

    // Directory finalization and rollback use the same no-overwrite protocol.
    var finalDirectorySource = Path.Combine(phase2Root, "DirectorySource");
    var finalDirectoryTemp = Path.Combine(phase2Root, ".~br-phase2-directory");
    var finalDirectoryTarget = Path.Combine(phase2Root, "DirectoryTarget");
    Directory.CreateDirectory(finalDirectorySource);
    File.WriteAllText(Path.Combine(finalDirectorySource, "child.txt"), "directory-child");
    var finalDirectoryIdentity = realIdentities.TryGetIdentity(finalDirectorySource, true);
    ExpectTrue(finalDirectoryIdentity is not null, "phase2 directory identity available");
    var finalDirectoryPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, phase2SemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), finalDirectorySource, finalDirectoryTemp, finalDirectoryTarget, true, finalDirectoryIdentity)]);
    ExpectTrue(TransactionPhase1Executor.Execute(finalDirectoryPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "phase2 directory Phase1 completed");
    ExpectTrue(TransactionPhase2Executor.Execute(finalDirectoryPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector).Success, "phase2 directory finalization completed");
    ExpectTrue(Directory.Exists(finalDirectoryTarget) && File.Exists(Path.Combine(finalDirectoryTarget, "child.txt")), "phase2 directory target contents preserved");
    ExpectTrue(realIdentities.TryGetIdentity(finalDirectoryTarget, true) == finalDirectoryIdentity, "phase2 directory identity preserved");
    ExpectTrue(TransactionRollbackExecutor.Execute(finalDirectoryPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector).Success, "rollback directory completed");
    ExpectTrue(Directory.Exists(finalDirectorySource) && File.Exists(Path.Combine(finalDirectorySource, "child.txt")), "rollback directory source restored");

    // A <-> B cycle: both names are vacated in Phase1, then final targets may safely exchange.
    var swapAPath = Path.Combine(phase2Root, "SwapA.txt");
    var swapBPath = Path.Combine(phase2Root, "SwapB.txt");
    File.WriteAllText(swapAPath, "from-A");
    File.WriteAllText(swapBPath, "from-B");
    var swapIdentityA = realIdentities.TryGetIdentity(swapAPath, false);
    var swapIdentityB = realIdentities.TryGetIdentity(swapBPath, false);
    ExpectTrue(swapIdentityA is not null && swapIdentityB is not null, "phase2 swap identities available");
    var realSwapPlan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        phase2SemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), swapAPath, Path.Combine(phase2Root, ".~br-phase2-swap-a"), swapBPath, false, swapIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), swapBPath, Path.Combine(phase2Root, ".~br-phase2-swap-b"), swapAPath, false, swapIdentityB),
        ]);
    ExpectTrue(TransactionPhase1Executor.Execute(realSwapPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "phase2 swap Phase1 completed");
    var realSwapPhase2 = TransactionPhase2Executor.Execute(realSwapPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(realSwapPhase2.Success, "phase2 A-B swap completed");
    Expect(File.ReadAllText(swapBPath), "from-A", "phase2 swap A moved to B");
    Expect(File.ReadAllText(swapAPath), "from-B", "phase2 swap B moved to A");
    ExpectTrue(realIdentities.TryGetIdentity(swapBPath, false) == swapIdentityA, "phase2 swap identity A preserved");
    ExpectTrue(realIdentities.TryGetIdentity(swapAPath, false) == swapIdentityB, "phase2 swap identity B preserved");
    var swapRollback = TransactionRollbackExecutor.Execute(realSwapPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(swapRollback.Success, "rollback A-B swap completed");
    Expect(File.ReadAllText(swapAPath), "from-A", "rollback swap restores A");
    Expect(File.ReadAllText(swapBPath), "from-B", "rollback swap restores B");

    // Case-only rename must preserve the exact requested target spelling and rollback to exact source spelling.
    var caseSource = Path.Combine(phase2Root, "casephoto.jpg");
    var caseTarget = Path.Combine(phase2Root, "CasePhoto.jpg");
    File.WriteAllText(caseSource, "case-only");
    var caseIdentity = realIdentities.TryGetIdentity(caseSource, false);
    ExpectTrue(caseIdentity is not null, "phase2 case-only identity available");
    var caseOnlyTransactionPlan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        phase2SemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), caseSource, Path.Combine(phase2Root, ".~br-phase2-case"), caseTarget, false, caseIdentity)]);
    ExpectTrue(TransactionPhase1Executor.Execute(caseOnlyTransactionPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "phase2 case-only Phase1 completed");
    var casePhase2 = TransactionPhase2Executor.Execute(caseOnlyTransactionPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(casePhase2.Success, "phase2 case-only completed");
    var actualTargetCase = exactInspector.TryGetActualPath(caseTarget, false, phase2Sem.IsCaseSensitive);
    ExpectTrue(actualTargetCase is not null && Path.GetFileName(actualTargetCase) == Path.GetFileName(caseTarget), "phase2 case-only exact target spelling");
    var caseRollback = TransactionRollbackExecutor.Execute(caseOnlyTransactionPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(caseRollback.Success, "rollback case-only completed");
    var actualSourceCase = exactInspector.TryGetActualPath(caseSource, false, phase2Sem.IsCaseSensitive);
    ExpectTrue(actualSourceCase is not null && Path.GetFileName(actualSourceCase) == Path.GetFileName(caseSource), "rollback case-only exact source spelling");

    // External target appearing after Phase1 must block Phase2 before any Temp -> Target mutation.
    var occupiedSource = Path.Combine(phase2Root, "OccupiedSource.txt");
    var occupiedTemp = Path.Combine(phase2Root, ".~br-phase2-occupied");
    var occupiedTarget = Path.Combine(phase2Root, "OccupiedTarget.txt");
    File.WriteAllText(occupiedSource, "owned");
    var occupiedIdentity = realIdentities.TryGetIdentity(occupiedSource, false);
    ExpectTrue(occupiedIdentity is not null, "phase2 occupied-target identity available");
    var occupiedFinalPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, phase2SemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), occupiedSource, occupiedTemp, occupiedTarget, false, occupiedIdentity)]);
    ExpectTrue(TransactionPhase1Executor.Execute(occupiedFinalPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "phase2 occupied-target Phase1 completed");
    File.WriteAllText(occupiedTarget, "external");
    var blockedPhase2 = TransactionPhase2Executor.Execute(occupiedFinalPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(blockedPhase2.State == Phase2ExecutionState.NotStarted && !blockedPhase2.HasFinalMutation, "phase2 external target blocks before mutation");
    ExpectTrue(blockedPhase2.Issues.Any(x => x.Code == "PHASE2_TARGET_ALREADY_EXISTS"), "phase2 external target issue");
    Expect(File.ReadAllText(occupiedTemp), "owned", "phase2 blocked temp object preserved");
    Expect(File.ReadAllText(occupiedTarget), "external", "phase2 external target never overwritten");
    File.Delete(occupiedTarget);
    ExpectTrue(TransactionRollbackExecutor.Execute(occupiedFinalPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector).Success, "rollback blocked Phase2 restores source");

    // Inject failure on the second final mutation. Rollback must normalize Target -> Temp and then restore all Sources.
    var partialA = Path.Combine(phase2Root, "PartialA.txt");
    var partialB = Path.Combine(phase2Root, "PartialB.txt");
    File.WriteAllText(partialA, "partial-a");
    File.WriteAllText(partialB, "partial-b");
    var partialIdentityA = realIdentities.TryGetIdentity(partialA, false);
    var partialIdentityB = realIdentities.TryGetIdentity(partialB, false);
    ExpectTrue(partialIdentityA is not null && partialIdentityB is not null, "phase2 partial identities available");
    var partialPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, phase2SemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), partialA, Path.Combine(phase2Root, ".~br-phase2-partial-a"), Path.Combine(phase2Root, "PartialA2.txt"), false, partialIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), partialB, Path.Combine(phase2Root, ".~br-phase2-partial-b"), Path.Combine(phase2Root, "PartialB2.txt"), false, partialIdentityB),
        ]);
    ExpectTrue(TransactionPhase1Executor.Execute(partialPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "phase2 partial Phase1 completed");
    var partialPhase2 = TransactionPhase2Executor.Execute(
        partialPlan, realFs, realSemantics, realIdentities, new FailOnNthMutationFileSystem(mutationFs, failOnCall: 2), exactInspector);
    ExpectTrue(partialPhase2.State == Phase2ExecutionState.FailedPartial && partialPhase2.RequiresRecovery, "phase2 partial failure state");
    ExpectTrue(partialPhase2.AppliedEntries.Count == 1 && partialPhase2.AppliedEntries[0].Ordinal == 0, "phase2 partial failure exact prefix");
    ExpectTrue(partialPhase2.Issues.Any(x => x.Code == "PHASE2_MOVE_FAILED"), "phase2 partial failure issue");
    ExpectTrue(File.Exists(partialPlan.Entries[0].TargetPath) && File.Exists(partialPlan.Entries[1].TemporaryPath), "phase2 partial mixed target-temp state");
    var partialRollback = TransactionRollbackExecutor.Execute(partialPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(partialRollback.Success, "rollback partial Phase2 completed");
    Expect(File.ReadAllText(partialA), "partial-a", "rollback partial restores A");
    Expect(File.ReadAllText(partialB), "partial-b", "rollback partial restores B");

    // Apply-then-throw on Phase2 must reconcile the applied final namespace instead of reporting zero mutation.
    var afterFinalSource = Path.Combine(phase2Root, "AfterFinal.txt");
    var afterFinalTemp = Path.Combine(phase2Root, ".~br-phase2-after-final");
    var afterFinalTarget = Path.Combine(phase2Root, "AfterFinal2.txt");
    File.WriteAllText(afterFinalSource, "after-final");
    var afterFinalIdentity = realIdentities.TryGetIdentity(afterFinalSource, false);
    ExpectTrue(afterFinalIdentity is not null, "phase2 apply-then-throw identity available");
    var afterFinalPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, phase2SemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), afterFinalSource, afterFinalTemp, afterFinalTarget, false, afterFinalIdentity)]);
    ExpectTrue(TransactionPhase1Executor.Execute(afterFinalPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "phase2 apply-then-throw Phase1 completed");
    var afterFinalResult = TransactionPhase2Executor.Execute(
        afterFinalPlan, realFs, realSemantics, realIdentities, new ApplyThenThrowOnNthMutationFileSystem(mutationFs, throwOnCall: 1), exactInspector);
    ExpectTrue(afterFinalResult.State == Phase2ExecutionState.FailedPartial && afterFinalResult.AppliedEntries.Count == 1, "phase2 apply-then-throw recovery state");
    ExpectTrue(afterFinalResult.Issues.Any(x => x.Code == "PHASE2_MOVE_EXCEPTION_AFTER_APPLY"), "phase2 apply-then-throw issue");
    ExpectTrue(File.Exists(afterFinalTarget) && !File.Exists(afterFinalTemp), "phase2 apply-then-throw observes target state");
    ExpectTrue(TransactionRollbackExecutor.Execute(afterFinalPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector).Success, "rollback apply-then-throw Phase2 completed");

    // Rollback itself may receive an exception after the filesystem already applied Target -> Temp.
    // It must report recovery-needed, preserve the observed mutation, and succeed when re-run.
    var rollbackThrowSource = Path.Combine(phase2Root, "RollbackThrow.txt");
    var rollbackThrowTemp = Path.Combine(phase2Root, ".~br-rollback-throw");
    var rollbackThrowTarget = Path.Combine(phase2Root, "RollbackThrow2.txt");
    File.WriteAllText(rollbackThrowSource, "rollback-throw");
    var rollbackThrowIdentity = realIdentities.TryGetIdentity(rollbackThrowSource, false);
    ExpectTrue(rollbackThrowIdentity is not null, "rollback apply-then-throw identity available");
    var rollbackThrowPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, phase2SemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), rollbackThrowSource, rollbackThrowTemp, rollbackThrowTarget, false, rollbackThrowIdentity)]);
    ExpectTrue(TransactionPhase1Executor.Execute(rollbackThrowPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "rollback apply-then-throw Phase1 completed");
    ExpectTrue(TransactionPhase2Executor.Execute(rollbackThrowPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector).Success, "rollback apply-then-throw Phase2 completed");
    var rollbackThrowResult = TransactionRollbackExecutor.Execute(
        rollbackThrowPlan, realFs, realSemantics, realIdentities, new ApplyThenThrowOnNthMutationFileSystem(mutationFs, throwOnCall: 1), exactInspector);
    ExpectTrue(!rollbackThrowResult.Success && rollbackThrowResult.HasMutation, "rollback apply-then-throw reports partial recovery");
    ExpectTrue(rollbackThrowResult.Issues.Any(x => x.Code == "ROLLBACK_R1_MOVE_EXCEPTION_AFTER_APPLY"), "rollback apply-then-throw issue");
    ExpectTrue(File.Exists(rollbackThrowTemp) && !File.Exists(rollbackThrowTarget), "rollback apply-then-throw observes temp state");
    var rollbackThrowRetry = TransactionRollbackExecutor.Execute(
        rollbackThrowPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(rollbackThrowRetry.Success, "rollback retry after apply-then-throw succeeds");
    Expect(File.ReadAllText(rollbackThrowSource), "rollback-throw", "rollback retry after apply-then-throw restores source");

    // Rollback also handles a partial Phase1 prefix directly from real filesystem state.
    var rollbackP1A = Path.Combine(phase2Root, "RollbackP1A.txt");
    var rollbackP1B = Path.Combine(phase2Root, "RollbackP1B.txt");
    File.WriteAllText(rollbackP1A, "rp1-a");
    File.WriteAllText(rollbackP1B, "rp1-b");
    var rollbackP1IdentityA = realIdentities.TryGetIdentity(rollbackP1A, false);
    var rollbackP1IdentityB = realIdentities.TryGetIdentity(rollbackP1B, false);
    ExpectTrue(rollbackP1IdentityA is not null && rollbackP1IdentityB is not null, "rollback partial Phase1 identities available");
    var rollbackP1Plan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, phase2SemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), rollbackP1A, Path.Combine(phase2Root, ".~br-rollback-p1-a"), Path.Combine(phase2Root, "RollbackP1A2.txt"), false, rollbackP1IdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), rollbackP1B, Path.Combine(phase2Root, ".~br-rollback-p1-b"), Path.Combine(phase2Root, "RollbackP1B2.txt"), false, rollbackP1IdentityB),
        ]);
    var rollbackP1Failure = TransactionPhase1Executor.Execute(
        rollbackP1Plan, realFs, realSemantics, realIdentities, new FailOnNthMutationFileSystem(mutationFs, failOnCall: 2));
    ExpectTrue(rollbackP1Failure.State == Phase1ExecutionState.FailedPartial, "rollback receives partial Phase1 state");
    var rollbackP1Result = TransactionRollbackExecutor.Execute(rollbackP1Plan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(rollbackP1Result.Success, "rollback partial Phase1 completed");
    Expect(File.ReadAllText(rollbackP1A), "rp1-a", "rollback partial Phase1 restores moved item");
    Expect(File.ReadAllText(rollbackP1B), "rp1-b", "rollback partial Phase1 preserves untouched item");

    // A foreign object recreating Source after Phase1 must block rollback rather than be overwritten.
    var conflictSource = Path.Combine(phase2Root, "ConflictSource.txt");
    var conflictTemp = Path.Combine(phase2Root, ".~br-rollback-conflict");
    var conflictTarget = Path.Combine(phase2Root, "ConflictTarget.txt");
    File.WriteAllText(conflictSource, "original-conflict");
    var conflictIdentity = realIdentities.TryGetIdentity(conflictSource, false);
    ExpectTrue(conflictIdentity is not null, "rollback conflict identity available");
    var conflictPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, phase2SemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), conflictSource, conflictTemp, conflictTarget, false, conflictIdentity)]);
    ExpectTrue(TransactionPhase1Executor.Execute(conflictPlan, realFs, realSemantics, realIdentities, mutationFs).Success, "rollback conflict Phase1 completed");
    File.WriteAllText(conflictSource, "external-conflict");
    var conflictRollback = TransactionRollbackExecutor.Execute(conflictPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector);
    ExpectTrue(!conflictRollback.Success && conflictRollback.Issues.Any(x => x.Code == "ROLLBACK_R2_DESTINATION_OCCUPIED"), "rollback external source occupancy blocks overwrite");
    Expect(File.ReadAllText(conflictSource), "external-conflict", "rollback external source preserved");
    Expect(File.ReadAllText(conflictTemp), "original-conflict", "rollback owned temp preserved after conflict");
    File.Delete(conflictSource);
    ExpectTrue(TransactionRollbackExecutor.Execute(conflictPlan, realFs, realSemantics, realIdentities, mutationFs, exactInspector).Success, "rollback retry succeeds after conflict removed");
    Expect(File.ReadAllText(conflictSource), "original-conflict", "rollback retry restores original object");
}
finally
{
    try { if (Directory.Exists(phase2Root)) Directory.Delete(phase2Root, recursive: true); } catch { }
}



// V0.7-A/B: append-only Journal + advisory state.json + read-only crash-recovery classification.
// These tests use dedicated temp sandboxes. The analyzer itself never mutates Source/Temp/Target.
var recoveryRoot = Path.Combine(Path.GetTempPath(), $"BatchRenamer-Recovery-Smoke-{Guid.NewGuid():N}");
var recoveryTransactionsRoot = Path.Combine(recoveryRoot, "transactions");
var recoveryDataRoot = Path.Combine(recoveryRoot, "data");
Directory.CreateDirectory(recoveryTransactionsRoot);
Directory.CreateDirectory(recoveryDataRoot);
try
{
    var recoveryFs = new WindowsReadOnlyFileSystem();
    var recoverySemanticsProvider = new WindowsPathSemanticsProvider();
    var recoveryIdentityProvider = new WindowsFileIdentityProvider();
    var recoveryMutationFs = new SystemRenameMutationFileSystem();
    var recoveryExactInspector = new SystemExactNamespaceInspector();
    var recoverySemantics = recoverySemanticsProvider.GetSemantics(recoveryDataRoot);
    var recoverySemanticsSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(
            recoveryDataRoot,
            recoverySemantics.IsCaseSensitive,
            recoverySemantics.IsReliable,
            recoverySemantics.MaxComponentLength,
            recoverySemantics.MaxPathLength,
            recoverySemantics.Source),
    };

    // Journal round-trip + plan binding + crash-truncated tail tolerance.
    var journalSource = Path.Combine(recoveryDataRoot, "JournalSource.txt");
    File.WriteAllText(journalSource, "journal");
    var journalIdentity = recoveryIdentityProvider.TryGetIdentity(journalSource, false);
    ExpectTrue(journalIdentity is not null, "journal identity available");
    var journalPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, recoverySemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), journalSource, Path.Combine(recoveryDataRoot, ".~br-journal"), Path.Combine(recoveryDataRoot, "JournalTarget.txt"), false, journalIdentity)]);
    var journalPersist = RenamePlanPersistence.PersistNew(journalPlan, recoveryTransactionsRoot);
    ExpectTrue(journalPersist.Success && journalPersist.TransactionDirectory is not null, "journal plan persisted");
    var journalDirectory = journalPersist.TransactionDirectory!;
    var journalIntent = TransactionJournal.Create(journalPlan, journalPlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp);
    var journalDone = TransactionJournal.Create(journalPlan, journalPlan.Entries[0], TransactionJournalEventKind.Done, TransactionJournalOperation.Phase1SourceToTemp);
    ExpectTrue(TransactionJournal.Append(journalDirectory, journalIntent).Success, "journal INTENT append");
    ExpectTrue(TransactionJournal.Append(journalDirectory, journalDone).Success, "journal DONE append");
    var journalLoad = TransactionJournal.Load(journalDirectory, journalPlan);
    ExpectTrue(journalLoad.Success && journalLoad.Events.Count == 2, "journal round-trip event count");
    ExpectTrue(journalLoad.Events[0].Kind == TransactionJournalEventKind.Intent && journalLoad.Events[1].Kind == TransactionJournalEventKind.Done, "journal preserves append order");
    var mismatchedJournalEvent = journalIntent with { EventId = Guid.NewGuid(), ItemId = Guid.NewGuid() };
    ExpectTrue(!TransactionJournal.Append(journalDirectory, mismatchedJournalEvent).Success, "journal rejects item mismatch");

    var journalPath = Path.Combine(journalDirectory, TransactionJournal.JournalFileName);
    using (var corruptTail = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
    {
        var tailBytes = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1");
        corruptTail.Write(tailBytes);
        corruptTail.Flush(flushToDisk: true);
    }
    var truncatedJournalLoad = TransactionJournal.Load(journalDirectory, journalPlan);
    ExpectTrue(truncatedJournalLoad.Success && truncatedJournalLoad.Events.Count == 2, "journal ignores crash-truncated final tail");
    ExpectTrue(truncatedJournalLoad.Issues.Any(x => x.Code == "JOURNAL_TRUNCATED_TAIL"), "journal reports truncated tail warning");

    // state.json is small, replaceable and explicitly advisory.
    var checkpoint = TransactionStateStore.Create(journalPlan, TransactionCheckpointPhase.Prepared, note: "smoke");
    var checkpointWrite = TransactionStateStore.Write(journalDirectory, checkpoint);
    ExpectTrue(checkpointWrite.Success, "state checkpoint write");
    var checkpointLoad = TransactionStateStore.Load(journalDirectory, journalPlan.TransactionId);
    ExpectTrue(checkpointLoad.Success && checkpointLoad.Checkpoint?.Phase == TransactionCheckpointPhase.Prepared, "state checkpoint round-trip");
    var checkpointUpdate = TransactionStateStore.Create(journalPlan, TransactionCheckpointPhase.RecoveryRequired, 0, "updated");
    ExpectTrue(TransactionStateStore.Write(journalDirectory, checkpointUpdate).Success, "state checkpoint replace");
    ExpectTrue(TransactionStateStore.Load(journalDirectory, journalPlan.TransactionId).Checkpoint?.Phase == TransactionCheckpointPhase.RecoveryRequired, "state checkpoint latest wins");

    // Recovery state machine: Source -> partial Phase1 -> all Temp -> partial Phase2 -> all Target -> rollback.
    var recoveryA = Path.Combine(recoveryDataRoot, "RecoveryA.txt");
    var recoveryB = Path.Combine(recoveryDataRoot, "RecoveryB.txt");
    File.WriteAllText(recoveryA, "recovery-a");
    File.WriteAllText(recoveryB, "recovery-b");
    var recoveryIdentityA = recoveryIdentityProvider.TryGetIdentity(recoveryA, false);
    var recoveryIdentityB = recoveryIdentityProvider.TryGetIdentity(recoveryB, false);
    ExpectTrue(recoveryIdentityA is not null && recoveryIdentityB is not null, "recovery identities available");
    var recoveryPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, recoverySemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), recoveryA, Path.Combine(recoveryDataRoot, ".~br-recovery-a"), Path.Combine(recoveryDataRoot, "RecoveryA2.txt"), false, recoveryIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), recoveryB, Path.Combine(recoveryDataRoot, ".~br-recovery-b"), Path.Combine(recoveryDataRoot, "RecoveryB2.txt"), false, recoveryIdentityB),
        ]);
    var recoveryPersist = RenamePlanPersistence.PersistNew(recoveryPlan, recoveryTransactionsRoot);
    ExpectTrue(recoveryPersist.Success && recoveryPersist.TransactionDirectory is not null, "recovery plan persisted");
    var recoveryDirectory = recoveryPersist.TransactionDirectory!;

    var recoveryInitial = TransactionRecoveryAnalyzer.Analyze(recoveryDirectory, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryInitial.State == TransactionRecoveryState.NotStarted, "recovery classifies not-started");
    ExpectTrue(!recoveryInitial.RequiresRecoveryAction, "recovery not-started requires no action");

    var recoveryPartialP1 = TransactionPhase1Executor.Execute(
        recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider,
        new FailOnNthMutationFileSystem(recoveryMutationFs, failOnCall: 2));
    ExpectTrue(recoveryPartialP1.State == Phase1ExecutionState.FailedPartial, "recovery setup partial Phase1");
    var recoveryP1Analysis = TransactionRecoveryAnalyzer.Analyze(recoveryDirectory, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryP1Analysis.State == TransactionRecoveryState.Phase1InProgress, "recovery classifies partial Phase1");
    ExpectTrue(recoveryP1Analysis.CanAutoRollback, "recovery partial Phase1 auto-rollback eligible");
    ExpectTrue(TransactionRollbackExecutor.Execute(recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs, recoveryExactInspector).Success, "recovery reset partial Phase1");

    ExpectTrue(TransactionPhase1Executor.Execute(recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs).Success, "recovery setup full Phase1");
    var recoveryP1DoneAnalysis = TransactionRecoveryAnalyzer.Analyze(recoveryDirectory, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryP1DoneAnalysis.State == TransactionRecoveryState.Phase1Applied, "recovery classifies completed Phase1");

    var recoveryPartialP2 = TransactionPhase2Executor.Execute(
        recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider,
        new FailOnNthMutationFileSystem(recoveryMutationFs, failOnCall: 2), recoveryExactInspector);
    ExpectTrue(recoveryPartialP2.State == Phase2ExecutionState.FailedPartial, "recovery setup partial Phase2");
    var recoveryP2Analysis = TransactionRecoveryAnalyzer.Analyze(recoveryDirectory, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryP2Analysis.State == TransactionRecoveryState.Phase2InProgress, "recovery classifies partial Phase2");
    ExpectTrue(recoveryP2Analysis.CanAutoRollback, "recovery partial Phase2 auto-rollback eligible");
    ExpectTrue(TransactionRollbackExecutor.Execute(recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs, recoveryExactInspector).Success, "recovery reset partial Phase2");

    ExpectTrue(TransactionPhase1Executor.Execute(recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs).Success, "recovery setup second full Phase1");
    ExpectTrue(TransactionPhase2Executor.Execute(recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs, recoveryExactInspector).Success, "recovery setup completed Phase2");
    var recoveryCompleted = TransactionRecoveryAnalyzer.Analyze(recoveryDirectory, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryCompleted.State == TransactionRecoveryState.Completed, "recovery classifies completed transaction");
    ExpectTrue(!recoveryCompleted.RequiresRecoveryAction, "recovery completed transaction requires no crash action");

    ExpectTrue(TransactionRollbackExecutor.Execute(recoveryPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs, recoveryExactInspector).Success, "recovery setup rollback complete");
    var rollbackDoneEvent = TransactionJournal.Create(recoveryPlan, recoveryPlan.Entries[0], TransactionJournalEventKind.Done, TransactionJournalOperation.RollbackTempToSource);
    ExpectTrue(TransactionJournal.Append(recoveryDirectory, rollbackDoneEvent).Success, "recovery records rollback evidence");
    var recoveryRolledBack = TransactionRecoveryAnalyzer.Analyze(recoveryDirectory, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryRolledBack.State == TransactionRecoveryState.RolledBack, "recovery distinguishes rolled-back from never-started");

    // Foreign namespace occupancy must override an otherwise recognizable Phase1 state.
    var externalSource = Path.Combine(recoveryDataRoot, "ExternalSource.txt");
    var externalTemp = Path.Combine(recoveryDataRoot, ".~br-recovery-external");
    var externalTarget = Path.Combine(recoveryDataRoot, "ExternalTarget.txt");
    File.WriteAllText(externalSource, "owned-external-test");
    var externalIdentity = recoveryIdentityProvider.TryGetIdentity(externalSource, false);
    ExpectTrue(externalIdentity is not null, "recovery external identity available");
    var externalPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, recoverySemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), externalSource, externalTemp, externalTarget, false, externalIdentity)]);
    var externalPersist = RenamePlanPersistence.PersistNew(externalPlan, recoveryTransactionsRoot);
    ExpectTrue(externalPersist.Success && externalPersist.TransactionDirectory is not null, "recovery external plan persisted");
    ExpectTrue(TransactionPhase1Executor.Execute(externalPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs).Success, "recovery external setup Phase1");
    File.WriteAllText(externalTarget, "foreign");
    var externalAnalysis = TransactionRecoveryAnalyzer.Analyze(externalPersist.TransactionDirectory!, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(externalAnalysis.State == TransactionRecoveryState.ExternallyModified, "recovery detects foreign target occupancy");
    ExpectTrue(!externalAnalysis.CanAutoRollback, "recovery refuses auto rollback after external modification");
    File.Delete(externalTarget);
    ExpectTrue(TransactionRollbackExecutor.Execute(externalPlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs, recoveryExactInspector).Success, "recovery external cleanup rollback");

    // Case-only rename must be classified using actual namespace spelling, not case-insensitive File.Exists alone.
    var recoveryCaseSource = Path.Combine(recoveryDataRoot, "recoverycase.jpg");
    var recoveryCaseTarget = Path.Combine(recoveryDataRoot, "RecoveryCase.jpg");
    File.WriteAllText(recoveryCaseSource, "case-recovery");
    var recoveryCaseIdentity = recoveryIdentityProvider.TryGetIdentity(recoveryCaseSource, false);
    ExpectTrue(recoveryCaseIdentity is not null, "recovery case-only identity available");
    var recoveryCasePlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, recoverySemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), recoveryCaseSource, Path.Combine(recoveryDataRoot, ".~br-recovery-case"), recoveryCaseTarget, false, recoveryCaseIdentity)]);
    var recoveryCasePersist = RenamePlanPersistence.PersistNew(recoveryCasePlan, recoveryTransactionsRoot);
    ExpectTrue(recoveryCasePersist.Success && recoveryCasePersist.TransactionDirectory is not null, "recovery case-only plan persisted");
    var recoveryCaseInitial = TransactionRecoveryAnalyzer.Analyze(recoveryCasePersist.TransactionDirectory!, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryCaseInitial.State == TransactionRecoveryState.NotStarted, "recovery case-only exact source spelling");
    ExpectTrue(TransactionPhase1Executor.Execute(recoveryCasePlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs).Success, "recovery case-only Phase1");
    ExpectTrue(TransactionPhase2Executor.Execute(recoveryCasePlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs, recoveryExactInspector).Success, "recovery case-only Phase2");
    var recoveryCaseCompleted = TransactionRecoveryAnalyzer.Analyze(recoveryCasePersist.TransactionDirectory!, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryExactInspector);
    ExpectTrue(recoveryCaseCompleted.State == TransactionRecoveryState.Completed, "recovery case-only exact target spelling");
    ExpectTrue(TransactionRollbackExecutor.Execute(recoveryCasePlan, recoveryFs, recoverySemanticsProvider, recoveryIdentityProvider, recoveryMutationFs, recoveryExactInspector).Success, "recovery case-only cleanup rollback");
}
finally
{
    try { if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, recursive: true); } catch { }
}



// V0.7.1: durable INTENT/DONE wiring around every real mutation + recovery orchestration.
var durableRoot = Path.Combine(Path.GetTempPath(), $"BatchRenamer-Durable-Smoke-{Guid.NewGuid():N}");
var durableTransactionsRoot = Path.Combine(durableRoot, "transactions");
var durableDataRoot = Path.Combine(durableRoot, "data");
Directory.CreateDirectory(durableTransactionsRoot);
Directory.CreateDirectory(durableDataRoot);
try
{
    var durableFs = new WindowsReadOnlyFileSystem();
    var durableSemanticsProvider = new WindowsPathSemanticsProvider();
    var durableIdentityProvider = new WindowsFileIdentityProvider();
    var durableMutationFs = new SystemRenameMutationFileSystem();
    var durableExactInspector = new SystemExactNamespaceInspector();
    var durableSemantics = durableSemanticsProvider.GetSemantics(durableDataRoot);
    var durableSemanticsSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(
            durableDataRoot,
            durableSemantics.IsCaseSensitive,
            durableSemantics.IsReliable,
            durableSemantics.MaxComponentLength,
            durableSemantics.MaxPathLength,
            durableSemantics.Source),
    };

    // Full durable two-phase execution: every namespace move must have INTENT then DONE.
    var durableSourceA = Path.Combine(durableDataRoot, "DurableA.txt");
    var durableSourceB = Path.Combine(durableDataRoot, "DurableB.txt");
    File.WriteAllText(durableSourceA, "durable-a");
    File.WriteAllText(durableSourceB, "durable-b");
    var durableIdentityA = durableIdentityProvider.TryGetIdentity(durableSourceA, false);
    var durableIdentityB = durableIdentityProvider.TryGetIdentity(durableSourceB, false);
    ExpectTrue(durableIdentityA is not null && durableIdentityB is not null, "durable execution identities available");
    var durablePlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), durableSourceA, Path.Combine(durableDataRoot, ".~br-durable-a"), Path.Combine(durableDataRoot, "DurableA2.txt"), false, durableIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), durableSourceB, Path.Combine(durableDataRoot, ".~br-durable-b"), Path.Combine(durableDataRoot, "DurableB2.txt"), false, durableIdentityB),
        ]);
    var durablePersist = RenamePlanPersistence.PersistNew(durablePlan, durableTransactionsRoot);
    ExpectTrue(durablePersist.Success && durablePersist.TransactionDirectory is not null, "durable execution plan persisted");
    var heldSession = TransactionSessionLease.TryAcquire(durablePersist.TransactionDirectory!);
    ExpectTrue(heldSession.Success && heldSession.Lease is not null, "transaction single-writer lease acquired");
    using (heldSession.Lease!)
    {
        var busyExecution = TransactionExecutionOrchestrator.Execute(
            durablePersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
            durableMutationFs, durableExactInspector);
        ExpectTrue(busyExecution.State == TransactionExecutionOverallState.SessionBusy, "concurrent transaction execution blocked by session lease");
        ExpectTrue(File.Exists(durableSourceA) && File.Exists(durableSourceB), "busy execution performs zero mutation");
    }

    var planLeaseBlockedWrite = false;
    using (var planLease = new PlanBoundTransactionJournalSink(durablePersist.TransactionDirectory!, durablePlan))
    {
        try
        {
            using var conflictingPlanWriter = new FileStream(
                durablePersist.PlanPath!, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException)
        {
            planLeaseBlockedWrite = true;
        }
    }
    ExpectTrue(planLeaseBlockedWrite, "durable plan lease blocks concurrent plan.json write");
    var durableExecution = TransactionExecutionOrchestrator.Execute(
        durablePersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(durableExecution.Success, "durable execution completed");
    Expect(File.ReadAllText(durablePlan.Entries[0].TargetPath), "durable-a", "durable execution target A content");
    Expect(File.ReadAllText(durablePlan.Entries[1].TargetPath), "durable-b", "durable execution target B content");
    var durableJournal = TransactionJournal.Load(durablePersist.TransactionDirectory!, durablePlan);
    ExpectTrue(durableJournal.Success && durableJournal.Events.Count == 8, "durable execution journal event count");
    foreach (var durableEntry in durablePlan.Entries.OrderBy(x => x.Ordinal))
    {
        var events = durableJournal.Events.Where(x => x.Ordinal == durableEntry.Ordinal).ToArray();
        ExpectTrue(events.Length == 4, $"durable entry {durableEntry.Ordinal} event count");
        ExpectTrue(events[0].Kind == TransactionJournalEventKind.Intent && events[0].Operation == TransactionJournalOperation.Phase1SourceToTemp, $"durable entry {durableEntry.Ordinal} Phase1 INTENT");
        ExpectTrue(events[1].Kind == TransactionJournalEventKind.Done && events[1].Operation == TransactionJournalOperation.Phase1SourceToTemp, $"durable entry {durableEntry.Ordinal} Phase1 DONE");
        ExpectTrue(events[2].Kind == TransactionJournalEventKind.Intent && events[2].Operation == TransactionJournalOperation.Phase2TempToTarget, $"durable entry {durableEntry.Ordinal} Phase2 INTENT");
        ExpectTrue(events[3].Kind == TransactionJournalEventKind.Done && events[3].Operation == TransactionJournalOperation.Phase2TempToTarget, $"durable entry {durableEntry.Ordinal} Phase2 DONE");
    }
    var durableState = TransactionStateStore.Load(durablePersist.TransactionDirectory!, durablePlan.TransactionId);
    ExpectTrue(durableState.Success && durableState.Checkpoint?.Phase == TransactionCheckpointPhase.Completed, "durable execution completed checkpoint");
    var durableReexecute = TransactionExecutionOrchestrator.Execute(
        durablePersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(durableReexecute.State == TransactionExecutionOverallState.RejectedByRecoveryState, "durable completed transaction cannot be re-executed under same id");

    // Durable wrapper must distinguish Source->Temp from Target->Temp for case-only rename.
    var durableCaseSource = Path.Combine(durableDataRoot, "durablecase.jpg");
    var durableCaseTarget = Path.Combine(durableDataRoot, "DurableCase.jpg");
    File.WriteAllText(durableCaseSource, "durable-case");
    var durableCaseIdentity = durableIdentityProvider.TryGetIdentity(durableCaseSource, false);
    ExpectTrue(durableCaseIdentity is not null, "durable case-only identity available");
    var durableCasePlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), durableCaseSource, Path.Combine(durableDataRoot, ".~br-durable-case"), durableCaseTarget, false, durableCaseIdentity)]);
    var durableCasePersist = RenamePlanPersistence.PersistNew(durableCasePlan, durableTransactionsRoot);
    ExpectTrue(durableCasePersist.Success && durableCasePersist.TransactionDirectory is not null, "durable case-only plan persisted");
    var durableCaseExecution = TransactionExecutionOrchestrator.Execute(
        durableCasePersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(durableCaseExecution.Success, "durable case-only execution completed");
    var durableCaseActual = durableExactInspector.TryGetActualPath(durableCaseTarget, false, durableSemantics.IsCaseSensitive);
    ExpectTrue(durableCaseActual is not null && string.Equals(Path.GetFileName(durableCaseActual), Path.GetFileName(durableCaseTarget), StringComparison.Ordinal), "durable case-only exact target spelling");
    var durableCaseJournal = TransactionJournal.Load(durableCasePersist.TransactionDirectory!, durableCasePlan);
    ExpectTrue(durableCaseJournal.Success && durableCaseJournal.Events.Count == 4, "durable case-only journal event count");
    ExpectTrue(durableCaseJournal.Events[0].Operation == TransactionJournalOperation.Phase1SourceToTemp
               && durableCaseJournal.Events[2].Operation == TransactionJournalOperation.Phase2TempToTarget,
        "durable case-only journal operation direction");

    // If durable INTENT cannot be written, no namespace mutation is allowed to start.
    var intentFailSource = Path.Combine(durableDataRoot, "IntentFail.txt");
    File.WriteAllText(intentFailSource, "intent-fail");
    var intentFailIdentity = durableIdentityProvider.TryGetIdentity(intentFailSource, false);
    ExpectTrue(intentFailIdentity is not null, "durable INTENT-failure identity available");
    var intentFailPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), intentFailSource, Path.Combine(durableDataRoot, ".~br-intent-fail"), Path.Combine(durableDataRoot, "IntentFail2.txt"), false, intentFailIdentity)]);
    var intentFailPersist = RenamePlanPersistence.PersistNew(intentFailPlan, durableTransactionsRoot);
    ExpectTrue(intentFailPersist.Success && intentFailPersist.TransactionDirectory is not null, "durable INTENT-failure plan persisted");
    var intentFailExecution = TransactionExecutionOrchestrator.Execute(
        intentFailPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector,
        new FailOnNthJournalSink(SystemTransactionJournalSink.Instance, failOnAppend: 1));
    ExpectTrue(intentFailExecution.State == TransactionExecutionOverallState.FailedBeforeMutation, "durable INTENT failure stops before mutation");
    ExpectTrue(File.Exists(intentFailSource) && !File.Exists(intentFailPlan.Entries[0].TemporaryPath) && !File.Exists(intentFailPlan.Entries[0].TargetPath), "durable INTENT failure preserves source namespace");

    // If DONE cannot be written after the move, the executor must reconcile the applied mutation and
    // require recovery. Recovery then journals rollback INTENT/DONE and restores Source.
    var doneFailSource = Path.Combine(durableDataRoot, "DoneFail.txt");
    File.WriteAllText(doneFailSource, "done-fail");
    var doneFailIdentity = durableIdentityProvider.TryGetIdentity(doneFailSource, false);
    ExpectTrue(doneFailIdentity is not null, "durable DONE-failure identity available");
    var doneFailPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), doneFailSource, Path.Combine(durableDataRoot, ".~br-done-fail"), Path.Combine(durableDataRoot, "DoneFail2.txt"), false, doneFailIdentity)]);
    var doneFailPersist = RenamePlanPersistence.PersistNew(doneFailPlan, durableTransactionsRoot);
    ExpectTrue(doneFailPersist.Success && doneFailPersist.TransactionDirectory is not null, "durable DONE-failure plan persisted");
    var doneFailExecution = TransactionExecutionOrchestrator.Execute(
        doneFailPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector,
        new FailOnNthJournalSink(SystemTransactionJournalSink.Instance, failOnAppend: 2));
    ExpectTrue(doneFailExecution.State == TransactionExecutionOverallState.RecoveryRequired, "durable DONE failure requires recovery");
    ExpectTrue(!File.Exists(doneFailSource) && File.Exists(doneFailPlan.Entries[0].TemporaryPath), "durable DONE failure observes applied Phase1 move");
    var doneFailAnalysis = TransactionRecoveryAnalyzer.Analyze(doneFailPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider, durableExactInspector);
    ExpectTrue(doneFailAnalysis.State == TransactionRecoveryState.Phase1Applied && doneFailAnalysis.CanAutoRollback, "durable DONE failure analyzable for auto rollback");
    var doneFailRecovery = TransactionRecoveryOrchestrator.Recover(
        doneFailPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(doneFailRecovery.Action == TransactionRecoveryAction.AutoRollbackCompleted, "durable DONE failure auto rollback completed");
    Expect(File.ReadAllText(doneFailSource), "done-fail", "durable DONE failure source restored");
    var doneFailReexecute = TransactionExecutionOrchestrator.Execute(
        doneFailPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(doneFailReexecute.State == TransactionExecutionOverallState.RejectedByRecoveryState, "durable rolled-back transaction cannot be re-executed under same id");
    var doneFailJournal = TransactionJournal.Load(doneFailPersist.TransactionDirectory!, doneFailPlan);
    ExpectTrue(doneFailJournal.Events.Any(x => x.Kind == TransactionJournalEventKind.Intent && x.Operation == TransactionJournalOperation.RollbackTempToSource), "durable rollback INTENT journaled");
    ExpectTrue(doneFailJournal.Events.Any(x => x.Kind == TransactionJournalEventKind.Done && x.Operation == TransactionJournalOperation.RollbackTempToSource), "durable rollback DONE journaled");

    // Crash window 1: INTENT was durable, but mutation never started. Recovery must make zero moves.
    var crashBeforeMoveSource = Path.Combine(durableDataRoot, "CrashBeforeMove.txt");
    File.WriteAllText(crashBeforeMoveSource, "before-move");
    var crashBeforeMoveIdentity = durableIdentityProvider.TryGetIdentity(crashBeforeMoveSource, false);
    ExpectTrue(crashBeforeMoveIdentity is not null, "crash-before-move identity available");
    var crashBeforeMovePlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), crashBeforeMoveSource, Path.Combine(durableDataRoot, ".~br-crash-before"), Path.Combine(durableDataRoot, "CrashBeforeMove2.txt"), false, crashBeforeMoveIdentity)]);
    var crashBeforeMovePersist = RenamePlanPersistence.PersistNew(crashBeforeMovePlan, durableTransactionsRoot);
    ExpectTrue(crashBeforeMovePersist.Success && crashBeforeMovePersist.TransactionDirectory is not null, "crash-before-move plan persisted");
    ExpectTrue(TransactionJournal.Append(
        crashBeforeMovePersist.TransactionDirectory!,
        TransactionJournal.Create(crashBeforeMovePlan, crashBeforeMovePlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp)).Success,
        "crash-before-move INTENT durable");
    var crashBeforeMoveRecovery = TransactionRecoveryOrchestrator.Recover(
        crashBeforeMovePersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(crashBeforeMoveRecovery.Action == TransactionRecoveryAction.NoActionNotStarted, "crash-before-move recovery makes no mutation");
    Expect(File.ReadAllText(crashBeforeMoveSource), "before-move", "crash-before-move source preserved");

    // Crash window 2: INTENT is durable and Source->Temp happened, but DONE was never written.
    var crashAfterMoveSource = Path.Combine(durableDataRoot, "CrashAfterMove.txt");
    File.WriteAllText(crashAfterMoveSource, "after-move");
    var crashAfterMoveIdentity = durableIdentityProvider.TryGetIdentity(crashAfterMoveSource, false);
    ExpectTrue(crashAfterMoveIdentity is not null, "crash-after-move identity available");
    var crashAfterMovePlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), crashAfterMoveSource, Path.Combine(durableDataRoot, ".~br-crash-after"), Path.Combine(durableDataRoot, "CrashAfterMove2.txt"), false, crashAfterMoveIdentity)]);
    var crashAfterMovePersist = RenamePlanPersistence.PersistNew(crashAfterMovePlan, durableTransactionsRoot);
    ExpectTrue(crashAfterMovePersist.Success && crashAfterMovePersist.TransactionDirectory is not null, "crash-after-move plan persisted");
    ExpectTrue(TransactionJournal.Append(
        crashAfterMovePersist.TransactionDirectory!,
        TransactionJournal.Create(crashAfterMovePlan, crashAfterMovePlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp)).Success,
        "crash-after-move INTENT durable");
    durableMutationFs.MoveFileNoOverwrite(crashAfterMovePlan.Entries[0].SourcePath, crashAfterMovePlan.Entries[0].TemporaryPath);
    var heldRecoverySession = TransactionSessionLease.TryAcquire(crashAfterMovePersist.TransactionDirectory!);
    ExpectTrue(heldRecoverySession.Success && heldRecoverySession.Lease is not null, "recovery single-writer lease acquired");
    using (heldRecoverySession.Lease!)
    {
        var busyRecovery = TransactionRecoveryOrchestrator.Recover(
            crashAfterMovePersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
            durableMutationFs, durableExactInspector);
        ExpectTrue(busyRecovery.Action == TransactionRecoveryAction.SessionBusy, "concurrent recovery blocked by session lease");
        ExpectTrue(!File.Exists(crashAfterMoveSource) && File.Exists(crashAfterMovePlan.Entries[0].TemporaryPath), "busy recovery performs zero mutation");
    }
    var crashAfterMoveRecovery = TransactionRecoveryOrchestrator.Recover(
        crashAfterMovePersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(crashAfterMoveRecovery.Action == TransactionRecoveryAction.AutoRollbackCompleted, "crash-after-move auto rollback completed");
    Expect(File.ReadAllText(crashAfterMoveSource), "after-move", "crash-after-move source restored");

    // Crash window 3: partial Phase2, one Temp->Target mutation applied after INTENT but before DONE.
    var crashP2SourceA = Path.Combine(durableDataRoot, "CrashP2A.txt");
    var crashP2SourceB = Path.Combine(durableDataRoot, "CrashP2B.txt");
    File.WriteAllText(crashP2SourceA, "crash-p2-a");
    File.WriteAllText(crashP2SourceB, "crash-p2-b");
    var crashP2IdentityA = durableIdentityProvider.TryGetIdentity(crashP2SourceA, false);
    var crashP2IdentityB = durableIdentityProvider.TryGetIdentity(crashP2SourceB, false);
    ExpectTrue(crashP2IdentityA is not null && crashP2IdentityB is not null, "crash-Phase2 identities available");
    var crashP2Plan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), crashP2SourceA, Path.Combine(durableDataRoot, ".~br-crash-p2-a"), Path.Combine(durableDataRoot, "CrashP2A2.txt"), false, crashP2IdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), crashP2SourceB, Path.Combine(durableDataRoot, ".~br-crash-p2-b"), Path.Combine(durableDataRoot, "CrashP2B2.txt"), false, crashP2IdentityB),
        ]);
    var crashP2Persist = RenamePlanPersistence.PersistNew(crashP2Plan, durableTransactionsRoot);
    ExpectTrue(crashP2Persist.Success && crashP2Persist.TransactionDirectory is not null, "crash-Phase2 plan persisted");
    ExpectTrue(TransactionPhase1Executor.Execute(crashP2Plan, durableFs, durableSemanticsProvider, durableIdentityProvider, durableMutationFs).Success, "crash-Phase2 setup full Phase1");
    ExpectTrue(TransactionJournal.Append(
        crashP2Persist.TransactionDirectory!,
        TransactionJournal.Create(crashP2Plan, crashP2Plan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase2TempToTarget)).Success,
        "crash-Phase2 INTENT durable");
    durableMutationFs.MoveFileNoOverwrite(crashP2Plan.Entries[0].TemporaryPath, crashP2Plan.Entries[0].TargetPath);
    var crashP2Analysis = TransactionRecoveryAnalyzer.Analyze(crashP2Persist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider, durableExactInspector);
    ExpectTrue(crashP2Analysis.State == TransactionRecoveryState.Phase2InProgress && crashP2Analysis.CanAutoRollback, "crash-Phase2 partial state auto-rollback eligible");
    var crashP2Recovery = TransactionRecoveryOrchestrator.Recover(
        crashP2Persist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(crashP2Recovery.Action == TransactionRecoveryAction.AutoRollbackCompleted, "crash-Phase2 auto rollback completed");
    Expect(File.ReadAllText(crashP2SourceA), "crash-p2-a", "crash-Phase2 source A restored");
    Expect(File.ReadAllText(crashP2SourceB), "crash-p2-b", "crash-Phase2 source B restored");

    // Crash window 4: the final Temp->Target mutation happened after INTENT but DONE was lost.
    // Filesystem + FileIdentity prove the intended final state, so recovery must accept Completed
    // rather than undoing a transaction that already reached every frozen Target.
    var crashFinalSource = Path.Combine(durableDataRoot, "CrashFinal.txt");
    File.WriteAllText(crashFinalSource, "crash-final");
    var crashFinalIdentity = durableIdentityProvider.TryGetIdentity(crashFinalSource, false);
    ExpectTrue(crashFinalIdentity is not null, "crash-final identity available");
    var crashFinalPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), crashFinalSource, Path.Combine(durableDataRoot, ".~br-crash-final"), Path.Combine(durableDataRoot, "CrashFinal2.txt"), false, crashFinalIdentity)]);
    var crashFinalPersist = RenamePlanPersistence.PersistNew(crashFinalPlan, durableTransactionsRoot);
    ExpectTrue(crashFinalPersist.Success && crashFinalPersist.TransactionDirectory is not null, "crash-final plan persisted");
    durableMutationFs.MoveFileNoOverwrite(crashFinalPlan.Entries[0].SourcePath, crashFinalPlan.Entries[0].TemporaryPath);
    ExpectTrue(TransactionJournal.Append(
        crashFinalPersist.TransactionDirectory!,
        TransactionJournal.Create(crashFinalPlan, crashFinalPlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase2TempToTarget)).Success,
        "crash-final Phase2 INTENT durable");
    durableMutationFs.MoveFileNoOverwrite(crashFinalPlan.Entries[0].TemporaryPath, crashFinalPlan.Entries[0].TargetPath);
    var crashFinalRecovery = TransactionRecoveryOrchestrator.Recover(
        crashFinalPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(crashFinalRecovery.Action == TransactionRecoveryAction.NoActionCompleted, "crash-final missing DONE accepts completed filesystem state");
    Expect(File.ReadAllText(crashFinalPlan.Entries[0].TargetPath), "crash-final", "crash-final target preserved");

    // Crash window 5: rollback itself was interrupted after durable Target->Temp INTENT and
    // namespace apply, before DONE. Analyzer must classify RollbackInProgress and resume idempotently.
    var crashRollbackSource = Path.Combine(durableDataRoot, "CrashRollback.txt");
    File.WriteAllText(crashRollbackSource, "crash-rollback");
    var crashRollbackIdentity = durableIdentityProvider.TryGetIdentity(crashRollbackSource, false);
    ExpectTrue(crashRollbackIdentity is not null, "crash-rollback identity available");
    var crashRollbackPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), crashRollbackSource, Path.Combine(durableDataRoot, ".~br-crash-rollback"), Path.Combine(durableDataRoot, "CrashRollback2.txt"), false, crashRollbackIdentity)]);
    var crashRollbackPersist = RenamePlanPersistence.PersistNew(crashRollbackPlan, durableTransactionsRoot);
    ExpectTrue(crashRollbackPersist.Success && crashRollbackPersist.TransactionDirectory is not null, "crash-rollback plan persisted");
    ExpectTrue(TransactionPhase1Executor.Execute(crashRollbackPlan, durableFs, durableSemanticsProvider, durableIdentityProvider, durableMutationFs).Success, "crash-rollback setup Phase1");
    ExpectTrue(TransactionPhase2Executor.Execute(crashRollbackPlan, durableFs, durableSemanticsProvider, durableIdentityProvider, durableMutationFs, durableExactInspector).Success, "crash-rollback setup Phase2");
    ExpectTrue(TransactionJournal.Append(
        crashRollbackPersist.TransactionDirectory!,
        TransactionJournal.Create(crashRollbackPlan, crashRollbackPlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.RollbackTargetToTemp)).Success,
        "crash-rollback Target->Temp INTENT durable");
    durableMutationFs.MoveFileNoOverwrite(crashRollbackPlan.Entries[0].TargetPath, crashRollbackPlan.Entries[0].TemporaryPath);
    var crashRollbackAnalysis = TransactionRecoveryAnalyzer.Analyze(crashRollbackPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider, durableExactInspector);
    ExpectTrue(crashRollbackAnalysis.State == TransactionRecoveryState.RollbackInProgress && crashRollbackAnalysis.CanAutoRollback, "crash-rollback classified rollback-in-progress");
    var crashRollbackRecovery = TransactionRecoveryOrchestrator.Recover(
        crashRollbackPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(crashRollbackRecovery.Action == TransactionRecoveryAction.AutoRollbackCompleted, "crash-rollback resumed automatically");
    Expect(File.ReadAllText(crashRollbackSource), "crash-rollback", "crash-rollback source restored");

    // Recovery must fail closed if it cannot durably journal the rollback INTENT.
    var recoveryJournalFailSource = Path.Combine(durableDataRoot, "RecoveryJournalFail.txt");
    File.WriteAllText(recoveryJournalFailSource, "recovery-journal-fail");
    var recoveryJournalFailIdentity = durableIdentityProvider.TryGetIdentity(recoveryJournalFailSource, false);
    ExpectTrue(recoveryJournalFailIdentity is not null, "recovery journal-failure identity available");
    var recoveryJournalFailPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), recoveryJournalFailSource, Path.Combine(durableDataRoot, ".~br-recovery-journal-fail"), Path.Combine(durableDataRoot, "RecoveryJournalFail2.txt"), false, recoveryJournalFailIdentity)]);
    var recoveryJournalFailPersist = RenamePlanPersistence.PersistNew(recoveryJournalFailPlan, durableTransactionsRoot);
    ExpectTrue(recoveryJournalFailPersist.Success && recoveryJournalFailPersist.TransactionDirectory is not null, "recovery journal-failure plan persisted");
    durableMutationFs.MoveFileNoOverwrite(recoveryJournalFailPlan.Entries[0].SourcePath, recoveryJournalFailPlan.Entries[0].TemporaryPath);
    var recoveryJournalFailResult = TransactionRecoveryOrchestrator.Recover(
        recoveryJournalFailPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector,
        new FailOnNthJournalSink(SystemTransactionJournalSink.Instance, failOnAppend: 1));
    ExpectTrue(recoveryJournalFailResult.Action == TransactionRecoveryAction.ManualRequired, "recovery journal failure requires manual recovery");
    ExpectTrue(!File.Exists(recoveryJournalFailSource) && File.Exists(recoveryJournalFailPlan.Entries[0].TemporaryPath), "recovery journal failure performs zero unjournaled rollback mutations");

    // ExternallyModified remains manual-only: orchestration must not touch owned Temp or foreign Target.
    var recoveryExternalSource = Path.Combine(durableDataRoot, "RecoveryExternal.txt");
    File.WriteAllText(recoveryExternalSource, "owned-recovery-external");
    var recoveryExternalIdentity = durableIdentityProvider.TryGetIdentity(recoveryExternalSource, false);
    ExpectTrue(recoveryExternalIdentity is not null, "recovery orchestrator external identity available");
    var recoveryExternalPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, durableSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), recoveryExternalSource, Path.Combine(durableDataRoot, ".~br-recovery-external-orchestrator"), Path.Combine(durableDataRoot, "RecoveryExternal2.txt"), false, recoveryExternalIdentity)]);
    var recoveryExternalPersist = RenamePlanPersistence.PersistNew(recoveryExternalPlan, durableTransactionsRoot);
    ExpectTrue(recoveryExternalPersist.Success && recoveryExternalPersist.TransactionDirectory is not null, "recovery orchestrator external plan persisted");
    durableMutationFs.MoveFileNoOverwrite(recoveryExternalPlan.Entries[0].SourcePath, recoveryExternalPlan.Entries[0].TemporaryPath);
    File.WriteAllText(recoveryExternalPlan.Entries[0].TargetPath, "foreign-recovery-external");
    var recoveryExternalResult = TransactionRecoveryOrchestrator.Recover(
        recoveryExternalPersist.TransactionDirectory!, durableFs, durableSemanticsProvider, durableIdentityProvider,
        durableMutationFs, durableExactInspector);
    ExpectTrue(recoveryExternalResult.Action == TransactionRecoveryAction.ManualRequired, "recovery orchestrator external modification stays manual-only");
    Expect(File.ReadAllText(recoveryExternalPlan.Entries[0].TemporaryPath), "owned-recovery-external", "recovery orchestrator preserves owned Temp on external conflict");
    Expect(File.ReadAllText(recoveryExternalPlan.Entries[0].TargetPath), "foreign-recovery-external", "recovery orchestrator never overwrites foreign Target");
}
finally
{
    try { if (Directory.Exists(durableRoot)) Directory.Delete(durableRoot, recursive: true); } catch { }
}



// V0.7.2: startup transaction discovery + fail-closed recovery gate.
var startupRoot = Path.Combine(Path.GetTempPath(), $"BatchRenamer-Startup-Smoke-{Guid.NewGuid():N}");
var startupTransactionsRoot = Path.Combine(startupRoot, "transactions");
var startupDataRoot = Path.Combine(startupRoot, "data");
Directory.CreateDirectory(startupTransactionsRoot);
Directory.CreateDirectory(startupDataRoot);
try
{
    var startupFs = new WindowsReadOnlyFileSystem();
    var startupSemanticsProvider = new WindowsPathSemanticsProvider();
    var startupIdentityProvider = new WindowsFileIdentityProvider();
    var startupMutationFs = new SystemRenameMutationFileSystem();
    var startupExactInspector = new SystemExactNamespaceInspector();
    var startupSemantics = startupSemanticsProvider.GetSemantics(startupDataRoot);
    var startupSemanticsSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(
            startupDataRoot,
            startupSemantics.IsCaseSensitive,
            startupSemantics.IsReliable,
            startupSemantics.MaxComponentLength,
            startupSemantics.MaxPathLength,
            startupSemantics.Source),
    };

    var startupMissingRoot = Path.Combine(startupRoot, "missing-transactions-root");
    var startupMissingRootScan = TransactionStartupDiscovery.Scan(
        startupMissingRoot, startupFs, startupSemanticsProvider, startupIdentityProvider, startupExactInspector);
    ExpectTrue(startupMissingRootScan.GateState == TransactionStartupGateState.Clear, "startup missing root is clear");
    ExpectTrue(startupMissingRootScan.CanStartNewTransaction, "startup missing root permits new transaction");
    ExpectTrue(!Directory.Exists(startupMissingRoot), "startup scan does not create missing root");

    // Prepared-but-never-started plans are safe historical metadata and must not block startup.
    var startupPreparedSource = Path.Combine(startupDataRoot, "Prepared.txt");
    File.WriteAllText(startupPreparedSource, "prepared");
    var startupPreparedIdentity = startupIdentityProvider.TryGetIdentity(startupPreparedSource, false);
    ExpectTrue(startupPreparedIdentity is not null, "startup prepared identity available");
    var startupPreparedPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), startupPreparedSource, Path.Combine(startupDataRoot, ".~br-startup-prepared"), Path.Combine(startupDataRoot, "Prepared2.txt"), false, startupPreparedIdentity)]);
    var startupPreparedPersist = RenamePlanPersistence.PersistNew(startupPreparedPlan, startupTransactionsRoot);
    ExpectTrue(startupPreparedPersist.Success, "startup prepared plan persisted");
    File.Delete(startupPreparedSource);
    ExpectTrue(!File.Exists(startupPreparedSource), "startup stale prepared source changed externally");

    // Completed transactions are terminal and must not block a later clean startup.
    var startupCompletedSource = Path.Combine(startupDataRoot, "Completed.txt");
    File.WriteAllText(startupCompletedSource, "completed");
    var startupCompletedIdentity = startupIdentityProvider.TryGetIdentity(startupCompletedSource, false);
    ExpectTrue(startupCompletedIdentity is not null, "startup completed identity available");
    var startupCompletedPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), startupCompletedSource, Path.Combine(startupDataRoot, ".~br-startup-completed"), Path.Combine(startupDataRoot, "Completed2.txt"), false, startupCompletedIdentity)]);
    var startupCompletedPersist = RenamePlanPersistence.PersistNew(startupCompletedPlan, startupTransactionsRoot);
    ExpectTrue(startupCompletedPersist.Success && startupCompletedPersist.TransactionDirectory is not null, "startup completed plan persisted");
    var startupCompletedExecution = TransactionExecutionOrchestrator.Execute(
        startupCompletedPersist.TransactionDirectory!, startupFs, startupSemanticsProvider, startupIdentityProvider,
        startupMutationFs, startupExactInspector);
    ExpectTrue(startupCompletedExecution.Success, "startup completed transaction executed");
    var startupCompletedLaterPath = Path.Combine(startupDataRoot, "CompletedLater.txt");
    File.Move(startupCompletedPlan.Entries[0].TargetPath, startupCompletedLaterPath);
    ExpectTrue(File.Exists(startupCompletedLaterPath), "startup completed target later changed externally");

    // A transaction recovered back to Source is also terminal and non-blocking.
    var startupRolledBackSource = Path.Combine(startupDataRoot, "RolledBack.txt");
    File.WriteAllText(startupRolledBackSource, "rolled-back");
    var startupRolledBackIdentity = startupIdentityProvider.TryGetIdentity(startupRolledBackSource, false);
    ExpectTrue(startupRolledBackIdentity is not null, "startup rolled-back identity available");
    var startupRolledBackPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), startupRolledBackSource, Path.Combine(startupDataRoot, ".~br-startup-rolledback"), Path.Combine(startupDataRoot, "RolledBack2.txt"), false, startupRolledBackIdentity)]);
    var startupRolledBackPersist = RenamePlanPersistence.PersistNew(startupRolledBackPlan, startupTransactionsRoot);
    ExpectTrue(startupRolledBackPersist.Success && startupRolledBackPersist.TransactionDirectory is not null, "startup rolled-back plan persisted");
    ExpectTrue(TransactionJournal.Append(
        startupRolledBackPersist.TransactionDirectory!,
        TransactionJournal.Create(startupRolledBackPlan, startupRolledBackPlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp)).Success,
        "startup rolled-back forward INTENT durable");
    startupMutationFs.MoveFileNoOverwrite(startupRolledBackPlan.Entries[0].SourcePath, startupRolledBackPlan.Entries[0].TemporaryPath);
    var startupRolledBackRecovery = TransactionRecoveryOrchestrator.Recover(
        startupRolledBackPersist.TransactionDirectory!, startupFs, startupSemanticsProvider, startupIdentityProvider,
        startupMutationFs, startupExactInspector);
    ExpectTrue(startupRolledBackRecovery.Action == TransactionRecoveryAction.AutoRollbackCompleted, "startup rolled-back recovery completed");
    File.Delete(startupRolledBackSource);
    ExpectTrue(!File.Exists(startupRolledBackSource), "startup rolled-back source later changed externally");

    Directory.CreateDirectory(Path.Combine(startupTransactionsRoot, "future-metadata-format"));
    var startupClearScan = TransactionStartupDiscovery.Scan(
        startupTransactionsRoot, startupFs, startupSemanticsProvider, startupIdentityProvider, startupExactInspector);
    ExpectTrue(startupClearScan.GateState == TransactionStartupGateState.Clear, "startup terminal/prepared catalog is clear");
    ExpectTrue(startupClearScan.CanStartNewTransaction, "startup terminal/prepared catalog permits new transaction");
    ExpectTrue(startupClearScan.Candidates.Any(x => x.TransactionId == startupPreparedPlan.TransactionId && x.Disposition == TransactionStartupDisposition.NotStarted), "startup discovers prepared transaction");
    ExpectTrue(startupClearScan.Candidates.Any(x => x.TransactionId == startupCompletedPlan.TransactionId && x.Disposition == TransactionStartupDisposition.Completed), "startup discovers completed transaction");
    ExpectTrue(startupClearScan.Candidates.Any(x => x.TransactionId == startupRolledBackPlan.TransactionId && x.Disposition == TransactionStartupDisposition.RolledBack), "startup discovers rolled-back transaction");
    ExpectTrue(startupClearScan.Issues.Any(x => x.Code == "STARTUP_UNKNOWN_DIRECTORY_IGNORED"), "startup ignores non-transaction directory with warning");

    // A recoverable partial transaction must fail the global startup gate closed.
    var startupRecoveryGateRoot = Path.Combine(startupRoot, "recovery-gate");
    var startupRecoveryGateData = Path.Combine(startupRoot, "recovery-data");
    Directory.CreateDirectory(startupRecoveryGateRoot);
    Directory.CreateDirectory(startupRecoveryGateData);
    var startupRecoveryGateSemantics = startupSemanticsProvider.GetSemantics(startupRecoveryGateData);
    var startupRecoveryGateSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(startupRecoveryGateData, startupRecoveryGateSemantics.IsCaseSensitive, startupRecoveryGateSemantics.IsReliable,
            startupRecoveryGateSemantics.MaxComponentLength, startupRecoveryGateSemantics.MaxPathLength, startupRecoveryGateSemantics.Source),
    };
    var startupPartialSource = Path.Combine(startupRecoveryGateData, "Partial.txt");
    File.WriteAllText(startupPartialSource, "partial");
    var startupPartialIdentity = startupIdentityProvider.TryGetIdentity(startupPartialSource, false);
    ExpectTrue(startupPartialIdentity is not null, "startup recovery identity available");
    var startupPartialPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupRecoveryGateSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), startupPartialSource, Path.Combine(startupRecoveryGateData, ".~br-startup-partial"), Path.Combine(startupRecoveryGateData, "Partial2.txt"), false, startupPartialIdentity)]);
    var startupPartialPersist = RenamePlanPersistence.PersistNew(startupPartialPlan, startupRecoveryGateRoot);
    ExpectTrue(startupPartialPersist.Success && startupPartialPersist.TransactionDirectory is not null, "startup recovery plan persisted");
    ExpectTrue(TransactionJournal.Append(
        startupPartialPersist.TransactionDirectory!,
        TransactionJournal.Create(startupPartialPlan, startupPartialPlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp)).Success,
        "startup recovery INTENT durable");
    startupMutationFs.MoveFileNoOverwrite(startupPartialPlan.Entries[0].SourcePath, startupPartialPlan.Entries[0].TemporaryPath);
    var startupRecoveryScan = TransactionStartupDiscovery.Scan(
        startupRecoveryGateRoot, startupFs, startupSemanticsProvider, startupIdentityProvider, startupExactInspector);
    ExpectTrue(startupRecoveryScan.GateState == TransactionStartupGateState.RecoveryRequired, "startup gate detects recoverable transaction");
    ExpectTrue(!startupRecoveryScan.CanStartNewTransaction, "startup recovery gate blocks new transaction");
    ExpectTrue(startupRecoveryScan.RecoveryRequiredCount == 1, "startup recovery-required count");
    ExpectTrue(startupRecoveryScan.Candidates.Single().Analysis?.CanAutoRollback == true, "startup recoverable candidate proves auto rollback eligibility");

    // V0.7.3: the coordinator may auto-rollback only an all-recoverable startup catalog.
    var startupRecoveryCoordinator = TransactionStartupRecoveryCoordinator.Run(
        startupRecoveryGateRoot, startupFs, startupSemanticsProvider, startupIdentityProvider,
        startupMutationFs, startupExactInspector);
    ExpectTrue(startupRecoveryCoordinator.State == TransactionStartupRecoveryCoordinatorState.AutoRecoveryCompleted, "startup coordinator auto-recovers eligible catalog");
    ExpectTrue(startupRecoveryCoordinator.AutoRecoveredCount == 1, "startup coordinator recovered count");
    ExpectTrue(startupRecoveryCoordinator.CanStartNewTransaction, "startup coordinator final gate clear");
    ExpectTrue(startupRecoveryCoordinator.FinalDiscovery.GateState == TransactionStartupGateState.Clear, "startup coordinator final discovery clear");
    Expect(File.ReadAllText(startupPartialSource), "partial", "startup coordinator restores source content");
    ExpectTrue(!File.Exists(startupPartialPlan.Entries[0].TemporaryPath), "startup coordinator vacates temp namespace");

    // A second startup pass after successful recovery must be idempotent and perform no mutation.
    var startupRecoveryCoordinatorSecond = TransactionStartupRecoveryCoordinator.Run(
        startupRecoveryGateRoot, startupFs, startupSemanticsProvider, startupIdentityProvider,
        startupMutationFs, startupExactInspector);
    ExpectTrue(startupRecoveryCoordinatorSecond.State == TransactionStartupRecoveryCoordinatorState.ClearNoAction, "startup coordinator second pass is no-op");
    ExpectTrue(startupRecoveryCoordinatorSecond.RecoveryResults.Count == 0 && !startupRecoveryCoordinatorSecond.PerformedRecoveryMutation, "startup coordinator second pass performs zero recovery mutation");

    // A live session must be reported as busy rather than mistaken for a crashed transaction.
    var startupBusyRoot = Path.Combine(startupRoot, "busy-gate");
    var startupBusyData = Path.Combine(startupRoot, "busy-data");
    Directory.CreateDirectory(startupBusyRoot);
    Directory.CreateDirectory(startupBusyData);
    var startupBusySemantics = startupSemanticsProvider.GetSemantics(startupBusyData);
    var startupBusySnapshot = new[]
    {
        new RenamePlanDirectorySemantics(startupBusyData, startupBusySemantics.IsCaseSensitive, startupBusySemantics.IsReliable,
            startupBusySemantics.MaxComponentLength, startupBusySemantics.MaxPathLength, startupBusySemantics.Source),
    };
    var startupBusySource = Path.Combine(startupBusyData, "Busy.txt");
    File.WriteAllText(startupBusySource, "busy");
    var startupBusyIdentity = startupIdentityProvider.TryGetIdentity(startupBusySource, false);
    ExpectTrue(startupBusyIdentity is not null, "startup busy identity available");
    var startupBusyPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupBusySnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), startupBusySource, Path.Combine(startupBusyData, ".~br-startup-busy"), Path.Combine(startupBusyData, "Busy2.txt"), false, startupBusyIdentity)]);
    var startupBusyPersist = RenamePlanPersistence.PersistNew(startupBusyPlan, startupBusyRoot);
    ExpectTrue(startupBusyPersist.Success && startupBusyPersist.TransactionDirectory is not null, "startup busy plan persisted");
    var startupHeldLease = TransactionSessionLease.TryAcquire(startupBusyPersist.TransactionDirectory!);
    ExpectTrue(startupHeldLease.Success && startupHeldLease.Lease is not null, "startup busy lease acquired");
    using (startupHeldLease.Lease!)
    {
        var startupBusyScan = TransactionStartupDiscovery.Scan(
            startupBusyRoot, startupFs, startupSemanticsProvider, startupIdentityProvider, startupExactInspector);
        ExpectTrue(startupBusyScan.GateState == TransactionStartupGateState.SessionBusy, "startup gate detects live session");
        ExpectTrue(!startupBusyScan.CanStartNewTransaction && startupBusyScan.SessionBusyCount == 1, "startup live session blocks new transaction");
        var startupBusyCoordinator = TransactionStartupRecoveryCoordinator.Run(
            startupBusyRoot, startupFs, startupSemanticsProvider, startupIdentityProvider,
            startupMutationFs, startupExactInspector);
        ExpectTrue(startupBusyCoordinator.State == TransactionStartupRecoveryCoordinatorState.BlockedSessionBusy, "startup coordinator respects live session");
        ExpectTrue(startupBusyCoordinator.RecoveryResults.Count == 0 && !startupBusyCoordinator.PerformedRecoveryMutation, "startup busy coordinator performs zero recovery mutation");
    }

    // A valid TransactionId directory with missing/corrupt plan is manual-only and dominates recovery.
    var startupManualRoot = Path.Combine(startupRoot, "manual-gate");
    Directory.CreateDirectory(startupManualRoot);
    var startupOrphanTransactionId = Guid.NewGuid();
    Directory.CreateDirectory(Path.Combine(startupManualRoot, startupOrphanTransactionId.ToString("N")));
    var startupManualScan = TransactionStartupDiscovery.Scan(
        startupManualRoot, startupFs, startupSemanticsProvider, startupIdentityProvider, startupExactInspector);
    ExpectTrue(startupManualScan.GateState == TransactionStartupGateState.ManualRequired, "startup gate detects transaction metadata loss");
    ExpectTrue(!startupManualScan.CanStartNewTransaction && startupManualScan.ManualRequiredCount == 1, "startup manual state blocks new transaction");

    var startupMixedRoot = Path.Combine(startupRoot, "mixed-gate");
    Directory.CreateDirectory(startupMixedRoot);
    var startupMixedData = Path.Combine(startupRoot, "mixed-data");
    Directory.CreateDirectory(startupMixedData);
    var startupMixedSemantics = startupSemanticsProvider.GetSemantics(startupMixedData);
    var startupMixedSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(startupMixedData, startupMixedSemantics.IsCaseSensitive, startupMixedSemantics.IsReliable,
            startupMixedSemantics.MaxComponentLength, startupMixedSemantics.MaxPathLength, startupMixedSemantics.Source),
    };
    var startupMixedSource = Path.Combine(startupMixedData, "MixedPartial.txt");
    File.WriteAllText(startupMixedSource, "mixed-partial");
    var startupMixedIdentity = startupIdentityProvider.TryGetIdentity(startupMixedSource, false);
    ExpectTrue(startupMixedIdentity is not null, "startup mixed identity available");
    var startupMixedPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupMixedSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), startupMixedSource, Path.Combine(startupMixedData, ".~br-startup-mixed"), Path.Combine(startupMixedData, "MixedPartial2.txt"), false, startupMixedIdentity)]);
    var startupMixedPersist = RenamePlanPersistence.PersistNew(startupMixedPlan, startupMixedRoot);
    ExpectTrue(startupMixedPersist.Success && startupMixedPersist.TransactionDirectory is not null, "startup mixed recoverable plan persisted");
    ExpectTrue(TransactionJournal.Append(
        startupMixedPersist.TransactionDirectory!,
        TransactionJournal.Create(startupMixedPlan, startupMixedPlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp)).Success,
        "startup mixed recovery INTENT durable");
    startupMutationFs.MoveFileNoOverwrite(startupMixedPlan.Entries[0].SourcePath, startupMixedPlan.Entries[0].TemporaryPath);
    Directory.CreateDirectory(Path.Combine(startupMixedRoot, Guid.NewGuid().ToString("N")));
    var startupMixedScan = TransactionStartupDiscovery.Scan(
        startupMixedRoot, startupFs, startupSemanticsProvider, startupIdentityProvider, startupExactInspector);
    ExpectTrue(startupMixedScan.GateState == TransactionStartupGateState.ManualRequired, "startup manual state dominates recoverable state");
    ExpectTrue(startupMixedScan.RecoveryRequiredCount == 1 && startupMixedScan.ManualRequiredCount == 1, "startup mixed gate preserves candidate counts");
    var startupMixedCoordinator = TransactionStartupRecoveryCoordinator.Run(
        startupMixedRoot, startupFs, startupSemanticsProvider, startupIdentityProvider,
        startupMutationFs, startupExactInspector);
    ExpectTrue(startupMixedCoordinator.State == TransactionStartupRecoveryCoordinatorState.ManualRequired, "startup coordinator manual state dominates");
    ExpectTrue(startupMixedCoordinator.RecoveryResults.Count == 0 && !startupMixedCoordinator.PerformedRecoveryMutation, "startup manual catalog performs zero automatic recovery mutation");
    ExpectTrue(File.Exists(startupMixedPlan.Entries[0].TemporaryPath) && !File.Exists(startupMixedPlan.Entries[0].SourcePath), "startup manual catalog leaves recoverable object untouched");

    // Multiple independent recoverable transactions are restored sequentially and must leave a Clear gate.
    var startupMultiRoot = Path.Combine(startupRoot, "multi-recovery-gate");
    var startupMultiData = Path.Combine(startupRoot, "multi-recovery-data");
    Directory.CreateDirectory(startupMultiRoot);
    Directory.CreateDirectory(startupMultiData);
    var startupMultiSemantics = startupSemanticsProvider.GetSemantics(startupMultiData);
    var startupMultiSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(startupMultiData, startupMultiSemantics.IsCaseSensitive, startupMultiSemantics.IsReliable,
            startupMultiSemantics.MaxComponentLength, startupMultiSemantics.MaxPathLength, startupMultiSemantics.Source),
    };
    var startupMultiPlans = new List<RenamePlan>();
    for (var i = 0; i < 2; i++)
    {
        var source = Path.Combine(startupMultiData, $"Multi{i}.txt");
        File.WriteAllText(source, $"multi-{i}");
        var identity = startupIdentityProvider.TryGetIdentity(source, false);
        ExpectTrue(identity is not null, $"startup multi identity {i} available");
        var startupMultiPlan = new RenamePlan(
            Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupMultiSnapshot,
            [new RenamePlanEntry(0, Guid.NewGuid(), source, Path.Combine(startupMultiData, $".~br-startup-multi-{i}"), Path.Combine(startupMultiData, $"Multi{i}-renamed.txt"), false, identity)]);
        var startupMultiPersisted = RenamePlanPersistence.PersistNew(startupMultiPlan, startupMultiRoot);
        ExpectTrue(startupMultiPersisted.Success && startupMultiPersisted.TransactionDirectory is not null, $"startup multi plan {i} persisted");
        ExpectTrue(TransactionJournal.Append(
            startupMultiPersisted.TransactionDirectory!,
            TransactionJournal.Create(startupMultiPlan, startupMultiPlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp)).Success,
            $"startup multi INTENT {i} durable");
        startupMutationFs.MoveFileNoOverwrite(startupMultiPlan.Entries[0].SourcePath, startupMultiPlan.Entries[0].TemporaryPath);
        startupMultiPlans.Add(startupMultiPlan);
    }
    var startupMultiCoordinator = TransactionStartupRecoveryCoordinator.Run(
        startupMultiRoot, startupFs, startupSemanticsProvider, startupIdentityProvider,
        startupMutationFs, startupExactInspector);
    ExpectTrue(startupMultiCoordinator.State == TransactionStartupRecoveryCoordinatorState.AutoRecoveryCompleted, "startup coordinator restores multiple transactions");
    ExpectTrue(startupMultiCoordinator.AutoRecoveredCount == 2 && startupMultiCoordinator.FinalDiscovery.GateState == TransactionStartupGateState.Clear, "startup coordinator multi final gate clear");
    Expect(File.ReadAllText(startupMultiPlans[0].Entries[0].SourcePath), "multi-0", "startup coordinator multi source 0 restored");
    Expect(File.ReadAllText(startupMultiPlans[1].Entries[0].SourcePath), "multi-1", "startup coordinator multi source 1 restored");

    // If a rollback mutation unexpectedly fails, the coordinator stops and leaves the gate closed.
    var startupFailureRoot = Path.Combine(startupRoot, "recovery-failure-gate");
    var startupFailureData = Path.Combine(startupRoot, "recovery-failure-data");
    Directory.CreateDirectory(startupFailureRoot);
    Directory.CreateDirectory(startupFailureData);
    var startupFailureSemantics = startupSemanticsProvider.GetSemantics(startupFailureData);
    var startupFailureSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(startupFailureData, startupFailureSemantics.IsCaseSensitive, startupFailureSemantics.IsReliable,
            startupFailureSemantics.MaxComponentLength, startupFailureSemantics.MaxPathLength, startupFailureSemantics.Source),
    };
    var startupFailureSource = Path.Combine(startupFailureData, "Failure.txt");
    File.WriteAllText(startupFailureSource, "failure");
    var startupFailureIdentity = startupIdentityProvider.TryGetIdentity(startupFailureSource, false);
    ExpectTrue(startupFailureIdentity is not null, "startup recovery-failure identity available");
    var startupFailurePlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, startupFailureSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), startupFailureSource, Path.Combine(startupFailureData, ".~br-startup-failure"), Path.Combine(startupFailureData, "Failure2.txt"), false, startupFailureIdentity)]);
    var startupFailurePersist = RenamePlanPersistence.PersistNew(startupFailurePlan, startupFailureRoot);
    ExpectTrue(startupFailurePersist.Success && startupFailurePersist.TransactionDirectory is not null, "startup recovery-failure plan persisted");
    ExpectTrue(TransactionJournal.Append(
        startupFailurePersist.TransactionDirectory!,
        TransactionJournal.Create(startupFailurePlan, startupFailurePlan.Entries[0], TransactionJournalEventKind.Intent, TransactionJournalOperation.Phase1SourceToTemp)).Success,
        "startup recovery-failure INTENT durable");
    startupMutationFs.MoveFileNoOverwrite(startupFailurePlan.Entries[0].SourcePath, startupFailurePlan.Entries[0].TemporaryPath);
    var startupFailingMutation = new FailOnNthMutationFileSystem(startupMutationFs, 1);
    var startupFailureCoordinator = TransactionStartupRecoveryCoordinator.Run(
        startupFailureRoot, startupFs, startupSemanticsProvider, startupIdentityProvider,
        startupFailingMutation, startupExactInspector);
    ExpectTrue(!startupFailureCoordinator.CanStartNewTransaction, "startup recovery failure keeps gate closed");
    ExpectTrue(startupFailureCoordinator.State is TransactionStartupRecoveryCoordinatorState.RecoveryIncomplete
        or TransactionStartupRecoveryCoordinatorState.ManualRequired, "startup recovery failure never reports clear");
    ExpectTrue(File.Exists(startupFailurePlan.Entries[0].TemporaryPath) && !File.Exists(startupFailurePlan.Entries[0].SourcePath), "startup recovery failure preserves unresolved owned temp");
}
finally
{
    try { if (Directory.Exists(startupRoot)) Directory.Delete(startupRoot, recursive: true); } catch { }
}



// V0.8.0: transaction history + durable explicit Undo foundation.
var undoRoot = Path.Combine(Path.GetTempPath(), $"BatchRenamer-Undo-Smoke-{Guid.NewGuid():N}");
var undoTransactionsRoot = Path.Combine(undoRoot, "transactions");
var undoDataRoot = Path.Combine(undoRoot, "data");
Directory.CreateDirectory(undoTransactionsRoot);
Directory.CreateDirectory(undoDataRoot);
try
{
    var undoFs = new WindowsReadOnlyFileSystem();
    var undoSemanticsProvider = new WindowsPathSemanticsProvider();
    var undoIdentityProvider = new WindowsFileIdentityProvider();
    var undoMutationFs = new SystemRenameMutationFileSystem();
    var undoExactInspector = new SystemExactNamespaceInspector();
    var undoSemantics = undoSemanticsProvider.GetSemantics(undoDataRoot);
    var undoSemanticsSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(
            undoDataRoot,
            undoSemantics.IsCaseSensitive,
            undoSemantics.IsReliable,
            undoSemantics.MaxComponentLength,
            undoSemantics.MaxPathLength,
            undoSemantics.Source),
    };

    // Prepared history remains non-undoable even if its Source later changes externally. No Journal
    // mutation evidence means this is historical dry-run metadata, not a crashed rename.
    var preparedHistorySource = Path.Combine(undoDataRoot, "PreparedHistory.txt");
    File.WriteAllText(preparedHistorySource, "prepared-history");
    var preparedHistoryIdentity = undoIdentityProvider.TryGetIdentity(preparedHistorySource, false);
    ExpectTrue(preparedHistoryIdentity is not null, "history prepared identity available");
    var preparedHistoryPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-1), RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), preparedHistorySource, Path.Combine(undoDataRoot, ".~br-prepared-history"), Path.Combine(undoDataRoot, "PreparedHistory2.txt"), false, preparedHistoryIdentity)]);
    var preparedHistoryPersist = RenamePlanPersistence.PersistNew(preparedHistoryPlan, undoTransactionsRoot);
    ExpectTrue(preparedHistoryPersist.Success, "history prepared plan persisted");
    File.Delete(preparedHistorySource);
    var preparedHistoryScan = TransactionHistoryService.Scan(
        undoTransactionsRoot, undoFs, undoSemanticsProvider, undoIdentityProvider, undoExactInspector);
    var preparedHistoryEntry = preparedHistoryScan.Entries.Single(x => x.TransactionId == preparedHistoryPlan.TransactionId);
    ExpectTrue(preparedHistoryEntry.Status == TransactionHistoryStatus.Prepared, "history keeps never-started stale plan as prepared");
    ExpectTrue(!preparedHistoryEntry.CanUndo, "history never exposes prepared plan as undoable");

    var undoSourceA = Path.Combine(undoDataRoot, "UndoA.txt");
    var undoSourceB = Path.Combine(undoDataRoot, "UndoB.txt");
    File.WriteAllText(undoSourceA, "undo-a");
    File.WriteAllText(undoSourceB, "undo-b");
    var undoIdentityA = undoIdentityProvider.TryGetIdentity(undoSourceA, false);
    var undoIdentityB = undoIdentityProvider.TryGetIdentity(undoSourceB, false);
    ExpectTrue(undoIdentityA is not null && undoIdentityB is not null, "undo identities available");
    var undoPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow, RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), undoSourceA, Path.Combine(undoDataRoot, ".~br-undo-a"), Path.Combine(undoDataRoot, "UndoA2.txt"), false, undoIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), undoSourceB, Path.Combine(undoDataRoot, ".~br-undo-b"), Path.Combine(undoDataRoot, "UndoB2.txt"), false, undoIdentityB),
        ]);
    var undoPersist = RenamePlanPersistence.PersistNew(undoPlan, undoTransactionsRoot);
    ExpectTrue(undoPersist.Success && undoPersist.TransactionDirectory is not null, "undo plan persisted");
    var undoExecute = TransactionExecutionOrchestrator.Execute(
        undoPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(undoExecute.Success, "undo setup durable transaction completed");
    Expect(File.ReadAllText(undoPlan.Entries[0].TargetPath), "undo-a", "undo setup target A content");
    Expect(File.ReadAllText(undoPlan.Entries[1].TargetPath), "undo-b", "undo setup target B content");

    var historyBeforeUndo = TransactionHistoryService.Scan(
        undoTransactionsRoot, undoFs, undoSemanticsProvider, undoIdentityProvider, undoExactInspector);
    var historyCompleted = historyBeforeUndo.Entries.Single(x => x.TransactionId == undoPlan.TransactionId);
    ExpectTrue(historyCompleted.Status == TransactionHistoryStatus.Completed, "history classifies completed transaction");
    ExpectTrue(historyCompleted.CanUndo, "history exposes safe completed transaction as undoable");
    ExpectTrue(historyBeforeUndo.UndoableCount == 1, "history undoable count");

    var undoResult = TransactionUndoOrchestrator.Undo(
        undoPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(undoResult.State == TransactionUndoState.Completed && undoResult.Success, "durable user Undo completed");
    ExpectTrue(undoResult.HasMutation, "durable user Undo reports mutation");
    Expect(File.ReadAllText(undoSourceA), "undo-a", "Undo restores source A");
    Expect(File.ReadAllText(undoSourceB), "undo-b", "Undo restores source B");
    ExpectTrue(!File.Exists(undoPlan.Entries[0].TargetPath) && !File.Exists(undoPlan.Entries[1].TargetPath), "Undo vacates final targets");
    ExpectTrue(!File.Exists(undoPlan.Entries[0].TemporaryPath) && !File.Exists(undoPlan.Entries[1].TemporaryPath), "Undo leaves no temp namespace");

    var undoJournal = TransactionJournal.Load(undoPersist.TransactionDirectory!, undoPlan);
    ExpectTrue(undoJournal.Events.Any(x => x.Kind == TransactionJournalEventKind.Intent && x.Operation == TransactionJournalOperation.RollbackTargetToTemp), "Undo journals rollback Target->Temp INTENT");
    ExpectTrue(undoJournal.Events.Any(x => x.Kind == TransactionJournalEventKind.Done && x.Operation == TransactionJournalOperation.RollbackTempToSource), "Undo journals rollback Temp->Source DONE");

    var historyAfterUndo = TransactionHistoryService.Scan(
        undoTransactionsRoot, undoFs, undoSemanticsProvider, undoIdentityProvider, undoExactInspector);
    var historyUndone = historyAfterUndo.Entries.Single(x => x.TransactionId == undoPlan.TransactionId);
    ExpectTrue(historyUndone.Status == TransactionHistoryStatus.Undone, "history classifies undone transaction");
    ExpectTrue(!historyUndone.CanUndo && historyAfterUndo.UndoableCount == 0, "history never offers Undo twice");

    var secondUndo = TransactionUndoOrchestrator.Undo(
        undoPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(secondUndo.State == TransactionUndoState.AlreadyUndone && secondUndo.Success, "second Undo is idempotent no-op");
    ExpectTrue(!secondUndo.HasMutation, "second Undo performs zero mutation");

    // Explicit Undo must preserve the already validated cycle and case-only semantics.
    var swapUndoA = Path.Combine(undoDataRoot, "SwapUndoA.txt");
    var swapUndoB = Path.Combine(undoDataRoot, "SwapUndoB.txt");
    File.WriteAllText(swapUndoA, "swap-undo-a");
    File.WriteAllText(swapUndoB, "swap-undo-b");
    var swapUndoIdentityA = undoIdentityProvider.TryGetIdentity(swapUndoA, false);
    var swapUndoIdentityB = undoIdentityProvider.TryGetIdentity(swapUndoB, false);
    ExpectTrue(swapUndoIdentityA is not null && swapUndoIdentityB is not null, "swap Undo identities available");
    var swapUndoPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddMilliseconds(100), RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), swapUndoA, Path.Combine(undoDataRoot, ".~br-swap-undo-a"), swapUndoB, false, swapUndoIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), swapUndoB, Path.Combine(undoDataRoot, ".~br-swap-undo-b"), swapUndoA, false, swapUndoIdentityB),
        ]);
    var swapUndoPersist = RenamePlanPersistence.PersistNew(swapUndoPlan, undoTransactionsRoot);
    ExpectTrue(swapUndoPersist.Success && swapUndoPersist.TransactionDirectory is not null, "swap Undo plan persisted");
    ExpectTrue(TransactionExecutionOrchestrator.Execute(
        swapUndoPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector).Success, "swap Undo setup transaction completed");
    Expect(File.ReadAllText(swapUndoA), "swap-undo-b", "swap Undo setup A receives B");
    Expect(File.ReadAllText(swapUndoB), "swap-undo-a", "swap Undo setup B receives A");
    var swapUndoResult = TransactionUndoOrchestrator.Undo(
        swapUndoPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(swapUndoResult.State == TransactionUndoState.Completed, "explicit Undo restores A-B swap");
    Expect(File.ReadAllText(swapUndoA), "swap-undo-a", "swap Undo restores A");
    Expect(File.ReadAllText(swapUndoB), "swap-undo-b", "swap Undo restores B");

    var caseUndoSource = Path.Combine(undoDataRoot, "CaseUndo.txt");
    var caseUndoTarget = Path.Combine(undoDataRoot, "caseundo.txt");
    File.WriteAllText(caseUndoSource, "case-undo");
    var caseUndoIdentity = undoIdentityProvider.TryGetIdentity(caseUndoSource, false);
    ExpectTrue(caseUndoIdentity is not null, "case-only Undo identity available");
    var caseUndoPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddMilliseconds(200), RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), caseUndoSource, Path.Combine(undoDataRoot, ".~br-case-undo"), caseUndoTarget, false, caseUndoIdentity)]);
    var caseUndoPersist = RenamePlanPersistence.PersistNew(caseUndoPlan, undoTransactionsRoot);
    ExpectTrue(caseUndoPersist.Success && caseUndoPersist.TransactionDirectory is not null, "case-only Undo plan persisted");
    ExpectTrue(TransactionExecutionOrchestrator.Execute(
        caseUndoPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector).Success, "case-only Undo setup transaction completed");
    var caseUndoActualTarget = undoExactInspector.TryGetActualPath(caseUndoTarget, false, undoSemantics.IsCaseSensitive);
    ExpectTrue(caseUndoActualTarget is not null, "case-only Undo actual target discoverable");
    ExpectTrue(string.Equals(
        Path.GetFileName(caseUndoActualTarget!),
        Path.GetFileName(caseUndoTarget), StringComparison.Ordinal), "case-only Undo setup exact target spelling");
    var caseUndoResult = TransactionUndoOrchestrator.Undo(
        caseUndoPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(caseUndoResult.State == TransactionUndoState.Completed, "explicit Undo restores case-only rename");
    var caseUndoActualSource = undoExactInspector.TryGetActualPath(caseUndoSource, false, undoSemantics.IsCaseSensitive);
    ExpectTrue(caseUndoActualSource is not null, "case-only Undo actual source discoverable");
    ExpectTrue(string.Equals(
        Path.GetFileName(caseUndoActualSource!),
        Path.GetFileName(caseUndoSource), StringComparison.Ordinal), "case-only Undo exact source spelling restored");

    // A completed transaction becomes non-undoable as soon as the frozen Target object is externally
    // removed/replaced. Undo must not overwrite the foreign replacement.
    var undoExternalSource = Path.Combine(undoDataRoot, "ExternalUndo.txt");
    File.WriteAllText(undoExternalSource, "owned-before-external");
    var undoExternalIdentity = undoIdentityProvider.TryGetIdentity(undoExternalSource, false);
    ExpectTrue(undoExternalIdentity is not null, "external Undo identity available");
    var undoExternalPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(1), RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), undoExternalSource, Path.Combine(undoDataRoot, ".~br-external-undo"), Path.Combine(undoDataRoot, "ExternalUndo2.txt"), false, undoExternalIdentity)]);
    var undoExternalPersist = RenamePlanPersistence.PersistNew(undoExternalPlan, undoTransactionsRoot);
    ExpectTrue(undoExternalPersist.Success && undoExternalPersist.TransactionDirectory is not null, "external Undo plan persisted");
    var undoExternalExecute = TransactionExecutionOrchestrator.Execute(
        undoExternalPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(undoExternalExecute.Success, "external Undo setup transaction completed");
    var parkedOwnedObject = Path.Combine(undoDataRoot, "ExternallyMovedOwnedObject.bin");
    File.Move(undoExternalPlan.Entries[0].TargetPath, parkedOwnedObject, overwrite: false);
    File.WriteAllText(undoExternalPlan.Entries[0].TargetPath, "foreign-target");

    var undoExternalHistory = TransactionHistoryService.Scan(
        undoTransactionsRoot, undoFs, undoSemanticsProvider, undoIdentityProvider, undoExactInspector);
    var undoExternalHistoryEntry = undoExternalHistory.Entries.Single(x => x.TransactionId == undoExternalPlan.TransactionId);
    ExpectTrue(undoExternalHistoryEntry.Status == TransactionHistoryStatus.ExternallyModified, "history detects externally modified completed transaction");
    ExpectTrue(!undoExternalHistoryEntry.CanUndo, "history suppresses unsafe Undo");
    var blockedUndo = TransactionUndoOrchestrator.Undo(
        undoExternalPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(blockedUndo.State == TransactionUndoState.NotEligible && !blockedUndo.HasMutation, "Undo refuses externally modified transaction");
    Expect(File.ReadAllText(undoExternalPlan.Entries[0].TargetPath), "foreign-target", "Undo never overwrites foreign target");
    Expect(File.ReadAllText(parkedOwnedObject), "owned-before-external", "Undo leaves externally moved owned object untouched");

    // Single-writer lease also applies to explicit Undo.
    var busySource = Path.Combine(undoDataRoot, "BusyUndo.txt");
    File.WriteAllText(busySource, "busy-undo");
    var busyIdentity = undoIdentityProvider.TryGetIdentity(busySource, false);
    ExpectTrue(busyIdentity is not null, "busy Undo identity available");
    var busyPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(2), RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), busySource, Path.Combine(undoDataRoot, ".~br-busy-undo"), Path.Combine(undoDataRoot, "BusyUndo2.txt"), false, busyIdentity)]);
    var busyPersist = RenamePlanPersistence.PersistNew(busyPlan, undoTransactionsRoot);
    ExpectTrue(busyPersist.Success && busyPersist.TransactionDirectory is not null, "busy Undo plan persisted");
    ExpectTrue(TransactionExecutionOrchestrator.Execute(
        busyPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector).Success, "busy Undo setup transaction completed");
    var heldUndoLease = TransactionSessionLease.TryAcquire(busyPersist.TransactionDirectory!);
    ExpectTrue(heldUndoLease.Success && heldUndoLease.Lease is not null, "busy Undo session lease acquired");
    using (heldUndoLease.Lease!)
    {
        var busyUndo = TransactionUndoOrchestrator.Undo(
            busyPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
            undoMutationFs, undoExactInspector);
        ExpectTrue(busyUndo.State == TransactionUndoState.SessionBusy && !busyUndo.HasMutation, "concurrent Undo blocked by session lease");
        Expect(File.ReadAllText(busyPlan.Entries[0].TargetPath), "busy-undo", "busy Undo leaves target untouched");
    }

    // If durable rollback INTENT cannot be appended, explicit Undo must perform zero namespace mutation
    // and remain safely retryable from the original Completed state.
    var undoIntentFailSource = Path.Combine(undoDataRoot, "UndoIntentFail.txt");
    File.WriteAllText(undoIntentFailSource, "undo-intent-fail");
    var undoIntentFailIdentity = undoIdentityProvider.TryGetIdentity(undoIntentFailSource, false);
    ExpectTrue(undoIntentFailIdentity is not null, "Undo INTENT-failure identity available");
    var undoIntentFailPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(3), RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), undoIntentFailSource, Path.Combine(undoDataRoot, ".~br-undo-intent-fail"), Path.Combine(undoDataRoot, "UndoIntentFail2.txt"), false, undoIntentFailIdentity)]);
    var undoIntentFailPersist = RenamePlanPersistence.PersistNew(undoIntentFailPlan, undoTransactionsRoot);
    ExpectTrue(undoIntentFailPersist.Success && undoIntentFailPersist.TransactionDirectory is not null, "Undo INTENT-failure plan persisted");
    ExpectTrue(TransactionExecutionOrchestrator.Execute(
        undoIntentFailPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector).Success, "Undo INTENT-failure setup transaction completed");
    var undoIntentFailResult = TransactionUndoOrchestrator.Undo(
        undoIntentFailPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector,
        new FailOnNthJournalSink(SystemTransactionJournalSink.Instance, failOnAppend: 1));
    ExpectTrue(undoIntentFailResult.State == TransactionUndoState.FailedNoMutation && !undoIntentFailResult.HasMutation, "Undo journal INTENT failure stops before mutation");
    Expect(File.ReadAllText(undoIntentFailPlan.Entries[0].TargetPath), "undo-intent-fail", "Undo journal INTENT failure preserves target namespace");

    // The same transaction can be safely retried after the injected metadata fault disappears.
    var undoIntentFailRetry = TransactionUndoOrchestrator.Undo(
        undoIntentFailPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(undoIntentFailRetry.State == TransactionUndoState.Completed, "Undo retry succeeds after journal INTENT failure");
    Expect(File.ReadAllText(undoIntentFailSource), "undo-intent-fail", "Undo retry restores source");

    // Crash-equivalent window during Undo: Target->Temp applied, but its DONE record fails. The Undo
    // result must explicitly require recovery and the existing recovery orchestrator must finish it.
    var undoDoneFailSource = Path.Combine(undoDataRoot, "UndoDoneFail.txt");
    File.WriteAllText(undoDoneFailSource, "undo-done-fail");
    var undoDoneFailIdentity = undoIdentityProvider.TryGetIdentity(undoDoneFailSource, false);
    ExpectTrue(undoDoneFailIdentity is not null, "Undo DONE-failure identity available");
    var undoDoneFailPlan = new RenamePlan(
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(4), RenamePlanner.CurrentSchemaVersion, undoSemanticsSnapshot,
        [new RenamePlanEntry(0, Guid.NewGuid(), undoDoneFailSource, Path.Combine(undoDataRoot, ".~br-undo-done-fail"), Path.Combine(undoDataRoot, "UndoDoneFail2.txt"), false, undoDoneFailIdentity)]);
    var undoDoneFailPersist = RenamePlanPersistence.PersistNew(undoDoneFailPlan, undoTransactionsRoot);
    ExpectTrue(undoDoneFailPersist.Success && undoDoneFailPersist.TransactionDirectory is not null, "Undo DONE-failure plan persisted");
    ExpectTrue(TransactionExecutionOrchestrator.Execute(
        undoDoneFailPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector).Success, "Undo DONE-failure setup transaction completed");
    var undoDoneFailResult = TransactionUndoOrchestrator.Undo(
        undoDoneFailPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector,
        new FailOnNthJournalSink(SystemTransactionJournalSink.Instance, failOnAppend: 2));
    ExpectTrue(undoDoneFailResult.State == TransactionUndoState.RecoveryRequired && undoDoneFailResult.HasMutation, "Undo DONE failure requires recovery after applied move");
    ExpectTrue(File.Exists(undoDoneFailPlan.Entries[0].TemporaryPath) && !File.Exists(undoDoneFailPlan.Entries[0].TargetPath), "Undo DONE failure exposes recoverable temp state");
    var undoDoneFailRecovery = TransactionRecoveryOrchestrator.Recover(
        undoDoneFailPersist.TransactionDirectory!, undoFs, undoSemanticsProvider, undoIdentityProvider,
        undoMutationFs, undoExactInspector);
    ExpectTrue(undoDoneFailRecovery.Action == TransactionRecoveryAction.AutoRollbackCompleted, "Undo DONE-failure state auto-recovers");
    Expect(File.ReadAllText(undoDoneFailSource), "undo-done-fail", "Undo DONE-failure recovery restores source");
}
finally
{
    try { if (Directory.Exists(undoRoot)) Directory.Delete(undoRoot, recursive: true); } catch { }
}


// ---------------- V0.9 Real UI command boundary coordinators ----------------
var v09Root = Path.Combine(Path.GetTempPath(), $"BatchRenamer_V09_Smoke_{Guid.NewGuid():N}");
var v09DataRoot = Path.Combine(v09Root, "data");
var v09TransactionsRoot = Path.Combine(v09Root, "transactions");
Directory.CreateDirectory(v09DataRoot);
try
{
    var v09Fs = new WindowsReadOnlyFileSystem();
    var v09SemanticsProvider = new WindowsPathSemanticsProvider();
    var v09IdentityProvider = new WindowsFileIdentityProvider();
    var v09MutationFs = new SystemRenameMutationFileSystem();
    var v09ExactInspector = new SystemExactNamespaceInspector();
    var v09Semantics = v09SemanticsProvider.GetSemantics(v09DataRoot);
    var v09SemanticsSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(
            v09DataRoot,
            v09Semantics.IsCaseSensitive,
            v09Semantics.IsReliable,
            v09Semantics.MaxComponentLength,
            v09Semantics.MaxPathLength,
            v09Semantics.Source),
    };

    var v09Source = Path.Combine(v09DataRoot, "UiExecute.txt");
    File.WriteAllText(v09Source, "ui-execute");
    var v09Identity = v09IdentityProvider.TryGetIdentity(v09Source, false);
    ExpectTrue(v09Identity is not null, "V0.9 UI command identity available");
    var v09Plan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RenamePlanner.CurrentSchemaVersion,
        v09SemanticsSnapshot,
        [new RenamePlanEntry(
            0,
            Guid.NewGuid(),
            v09Source,
            Path.Combine(v09DataRoot, $".~br-v09-{Guid.NewGuid():N}"),
            Path.Combine(v09DataRoot, "UiExecute_Renamed.txt"),
            false,
            v09Identity)]);

    var v09CatalogLease = TransactionCatalogLease.TryAcquire(v09TransactionsRoot);
    ExpectTrue(v09CatalogLease.Success && v09CatalogLease.Lease is not null, "V0.9 transaction catalog lease acquired");
    using (v09CatalogLease.Lease!)
    {
        var busyExecute = TransactionNewExecutionCoordinator.Execute(
            v09Plan,
            v09TransactionsRoot,
            v09Fs,
            v09SemanticsProvider,
            v09IdentityProvider,
            v09MutationFs,
            v09ExactInspector);
        ExpectTrue(busyExecute.State == TransactionNewExecutionState.CatalogBusy, "V0.9 concurrent new transaction blocked by catalog lease");
        ExpectTrue(File.Exists(v09Source) && !File.Exists(v09Plan.Entries[0].TargetPath), "V0.9 catalog-busy execution performs zero mutation");
    }

    var v09Execute = TransactionNewExecutionCoordinator.Execute(
        v09Plan,
        v09TransactionsRoot,
        v09Fs,
        v09SemanticsProvider,
        v09IdentityProvider,
        v09MutationFs,
        v09ExactInspector);
    ExpectTrue(v09Execute.State == TransactionNewExecutionState.Completed && v09Execute.Success, "V0.9 coordinated real execution completed");
    Expect(File.ReadAllText(v09Plan.Entries[0].TargetPath), "ui-execute", "V0.9 coordinated execution target content");
    var v09TransactionDirectory = v09Execute.Persistence?.TransactionDirectory
        ?? throw new InvalidOperationException("V0.9 coordinated execution did not expose the persisted transaction directory.");
    ExpectTrue(true, "V0.9 coordinated execution persisted frozen transaction");

    var v09History = TransactionHistoryService.Scan(
        v09TransactionsRoot,
        v09Fs,
        v09SemanticsProvider,
        v09IdentityProvider,
        v09ExactInspector);
    var v09HistoryEntry = v09History.Entries.Single(x => x.TransactionId == v09Plan.TransactionId);
    ExpectTrue(v09HistoryEntry.Status == TransactionHistoryStatus.Completed && v09HistoryEntry.CanUndo, "V0.9 completed UI transaction advertised as safely undoable");

    var v09UndoBusyLease = TransactionCatalogLease.TryAcquire(v09TransactionsRoot);
    ExpectTrue(v09UndoBusyLease.Success && v09UndoBusyLease.Lease is not null, "V0.9 Undo catalog lease acquired for contention test");
    using (v09UndoBusyLease.Lease!)
    {
        var busyUndo = TransactionUserUndoCoordinator.Undo(
            v09TransactionDirectory,
            v09TransactionsRoot,
            v09Fs,
            v09SemanticsProvider,
            v09IdentityProvider,
            v09MutationFs,
            v09ExactInspector);
        ExpectTrue(busyUndo.State == TransactionUserUndoCoordinatorState.CatalogBusy, "V0.9 concurrent Undo blocked by catalog lease");
        Expect(File.ReadAllText(v09Plan.Entries[0].TargetPath), "ui-execute", "V0.9 catalog-busy Undo performs zero mutation");
    }

    var v09Undo = TransactionUserUndoCoordinator.Undo(
        v09TransactionDirectory,
        v09TransactionsRoot,
        v09Fs,
        v09SemanticsProvider,
        v09IdentityProvider,
        v09MutationFs,
        v09ExactInspector);
    ExpectTrue(v09Undo.Success && v09Undo.State == TransactionUserUndoCoordinatorState.Completed, "V0.9 coordinated user Undo completed");
    Expect(File.ReadAllText(v09Source), "ui-execute", "V0.9 coordinated Undo restores source");

    // A real execution failure after the first namespace mutation is never surfaced as a half batch
    // when the durable recovery analyzer proves that automatic rollback is safe.
    var v09FailA = Path.Combine(v09DataRoot, "FailA.txt");
    var v09FailB = Path.Combine(v09DataRoot, "FailB.txt");
    File.WriteAllText(v09FailA, "fail-a");
    File.WriteAllText(v09FailB, "fail-b");
    var v09FailIdentityA = v09IdentityProvider.TryGetIdentity(v09FailA, false);
    var v09FailIdentityB = v09IdentityProvider.TryGetIdentity(v09FailB, false);
    ExpectTrue(v09FailIdentityA is not null && v09FailIdentityB is not null, "V0.9 failure-recovery identities available");
    var v09FailPlan = new RenamePlan(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow.AddSeconds(1),
        RenamePlanner.CurrentSchemaVersion,
        v09SemanticsSnapshot,
        [
            new RenamePlanEntry(0, Guid.NewGuid(), v09FailA, Path.Combine(v09DataRoot, $".~br-v09-faila-{Guid.NewGuid():N}"), Path.Combine(v09DataRoot, "FailA2.txt"), false, v09FailIdentityA),
            new RenamePlanEntry(1, Guid.NewGuid(), v09FailB, Path.Combine(v09DataRoot, $".~br-v09-failb-{Guid.NewGuid():N}"), Path.Combine(v09DataRoot, "FailB2.txt"), false, v09FailIdentityB),
        ]);
    var v09FailResult = TransactionNewExecutionCoordinator.Execute(
        v09FailPlan,
        v09TransactionsRoot,
        v09Fs,
        v09SemanticsProvider,
        v09IdentityProvider,
        new FailOnNthMutationFileSystem(v09MutationFs, failOnCall: 2),
        v09ExactInspector);
    ExpectTrue(v09FailResult.State == TransactionNewExecutionState.RolledBackAfterFailure && v09FailResult.WasSafelyRolledBack, "V0.9 partial execution auto-rolls back under UI command boundary");
    Expect(File.ReadAllText(v09FailA), "fail-a", "V0.9 auto-rollback restores first source");
    Expect(File.ReadAllText(v09FailB), "fail-b", "V0.9 auto-rollback preserves second source");
}
finally
{
    try { if (Directory.Exists(v09Root)) Directory.Delete(v09Root, recursive: true); } catch { }
}



// V0.10 release-candidate retention: bound full transaction metadata to approximately the newest
// 20 terminal records, purge abandoned prepared plans after a grace period, and never touch user
// Source/Target namespace while housekeeping transaction metadata.
var v10Root = Path.Combine(Path.GetTempPath(), $"BatchRenamer-V010-{Guid.NewGuid():N}");
var v10DataRoot = Path.Combine(v10Root, "data");
var v10TransactionsRoot = Path.Combine(v10Root, "transactions");
Directory.CreateDirectory(v10DataRoot);
Directory.CreateDirectory(v10TransactionsRoot);
try
{
    ExpectTrue(TransactionRetentionService.DefaultMaxTerminalTransactions == 20, "V0.10 default retention keeps approximately 20 terminal transactions");

    var v10Fs = new WindowsReadOnlyFileSystem();
    var v10SemanticsProvider = new WindowsPathSemanticsProvider();
    var v10IdentityProvider = new WindowsFileIdentityProvider();
    var v10MutationFs = new SystemRenameMutationFileSystem();
    var v10ExactInspector = new SystemExactNamespaceInspector();
    var v10Semantics = v10SemanticsProvider.GetSemantics(v10DataRoot);
    var v10SemanticsSnapshot = new[]
    {
        new RenamePlanDirectorySemantics(
            v10DataRoot,
            v10Semantics.IsCaseSensitive,
            v10Semantics.IsReliable,
            v10Semantics.MaxComponentLength,
            v10Semantics.MaxPathLength,
            v10Semantics.Source),
    };
    var retentionNow = DateTimeOffset.UtcNow;
    var completed = new List<(RenamePlan Plan, string TransactionDirectory, string TargetPath, string Content)>();

    for (var i = 0; i < 5; i++)
    {
        var source = Path.Combine(v10DataRoot, $"terminal-{i}.txt");
        var target = Path.Combine(v10DataRoot, $"terminal-{i}-done.txt");
        var content = $"terminal-content-{i}";
        File.WriteAllText(source, content);
        var identity = v10IdentityProvider.TryGetIdentity(source, false);
        ExpectTrue(identity is not null, $"V0.10 retention terminal identity {i} available");
        var v10TerminalPlan = new RenamePlan(
            Guid.NewGuid(),
            retentionNow.AddMinutes(-(i + 1)),
            RenamePlanner.CurrentSchemaVersion,
            v10SemanticsSnapshot,
            [new RenamePlanEntry(
                0,
                Guid.NewGuid(),
                source,
                Path.Combine(v10DataRoot, $".~br-v10-terminal-{i}-{Guid.NewGuid():N}"),
                target,
                false,
                identity)]);
        var execute = TransactionNewExecutionCoordinator.Execute(
            v10TerminalPlan,
            v10TransactionsRoot,
            v10Fs,
            v10SemanticsProvider,
            v10IdentityProvider,
            v10MutationFs,
            v10ExactInspector);
        ExpectTrue(execute.Success, $"V0.10 retention terminal transaction {i} completed");
        var directory = execute.Persistence?.TransactionDirectory
            ?? throw new InvalidOperationException("V0.10 terminal transaction directory missing.");
        completed.Add((v10TerminalPlan, directory, target, content));
    }

    RenamePlan BuildPreparedPlan(string stem, DateTimeOffset createdAt)
    {
        var source = Path.Combine(v10DataRoot, $"{stem}.txt");
        File.WriteAllText(source, stem);
        var identity = v10IdentityProvider.TryGetIdentity(source, false);
        if (identity is null) throw new InvalidOperationException($"V0.10 prepared identity unavailable: {stem}");
        return new RenamePlan(
            Guid.NewGuid(),
            createdAt,
            RenamePlanner.CurrentSchemaVersion,
            v10SemanticsSnapshot,
            [new RenamePlanEntry(
                0,
                Guid.NewGuid(),
                source,
                Path.Combine(v10DataRoot, $".~br-v10-{stem}-{Guid.NewGuid():N}"),
                Path.Combine(v10DataRoot, $"{stem}-done.txt"),
                false,
                identity)]);
    }

    var stalePreparedPlan = BuildPreparedPlan("prepared-stale", retentionNow.AddDays(-3));
    var stalePrepared = RenamePlanPersistence.PersistNew(stalePreparedPlan, v10TransactionsRoot);
    var stalePreparedDirectory = stalePrepared.TransactionDirectory
        ?? throw new InvalidOperationException("V0.10 stale prepared transaction directory missing.");
    ExpectTrue(stalePrepared.Success, "V0.10 stale prepared plan persisted");

    var freshPreparedPlan = BuildPreparedPlan("prepared-fresh", retentionNow.AddHours(-1));
    var freshPrepared = RenamePlanPersistence.PersistNew(freshPreparedPlan, v10TransactionsRoot);
    var freshPreparedDirectory = freshPrepared.TransactionDirectory
        ?? throw new InvalidOperationException("V0.10 fresh prepared transaction directory missing.");
    ExpectTrue(freshPrepared.Success, "V0.10 fresh prepared plan persisted");

    var unknownPreparedPlan = BuildPreparedPlan("prepared-unknown", retentionNow.AddDays(-4));
    var unknownPrepared = RenamePlanPersistence.PersistNew(unknownPreparedPlan, v10TransactionsRoot);
    var unknownDirectory = unknownPrepared.TransactionDirectory
        ?? throw new InvalidOperationException("V0.10 unknown-content prepared directory missing.");
    File.WriteAllText(Path.Combine(unknownDirectory, "keep.me"), "external metadata sentinel");
    ExpectTrue(unknownPrepared.Success, "V0.10 unknown-content prepared plan persisted");

    var retention = TransactionRetentionService.Cleanup(
        v10TransactionsRoot,
        v10Fs,
        v10SemanticsProvider,
        v10IdentityProvider,
        v10ExactInspector,
        new TransactionRetentionPolicy(3, TimeSpan.FromDays(2)),
        retentionNow);

    ExpectTrue(retention.DeletedCount == 3, "V0.10 retention deletes only two old terminal records plus stale prepared metadata");
    ExpectTrue(Directory.Exists(completed[0].TransactionDirectory)
               && Directory.Exists(completed[1].TransactionDirectory)
               && Directory.Exists(completed[2].TransactionDirectory),
        "V0.10 retention keeps newest terminal metadata window");
    ExpectTrue(!Directory.Exists(completed[3].TransactionDirectory)
               && !Directory.Exists(completed[4].TransactionDirectory),
        "V0.10 retention removes terminal metadata beyond configured window");
    ExpectTrue(!Directory.Exists(stalePreparedDirectory),
        "V0.10 retention removes abandoned stale prepared plan");
    ExpectTrue(Directory.Exists(freshPreparedDirectory),
        "V0.10 retention keeps fresh prepared plan during grace period");
    ExpectTrue(Directory.Exists(unknownDirectory) && File.Exists(Path.Combine(unknownDirectory, "keep.me")),
        "V0.10 retention refuses transaction directory containing unknown metadata");
    ExpectTrue(retention.Issues.Any(x => x.Code == "RETENTION_UNKNOWN_METADATA_PRESENT"),
        "V0.10 retention reports unknown metadata safety skip");

    for (var i = 0; i < completed.Count; i++)
        Expect(File.ReadAllText(completed[i].TargetPath), completed[i].Content, $"V0.10 retention never touches user target {i}");
    Expect(File.ReadAllText(stalePreparedPlan.Entries[0].SourcePath), "prepared-stale", "V0.10 retention never touches stale prepared source");
}
finally
{
    try { if (Directory.Exists(v10Root)) Directory.Delete(v10Root, recursive: true); } catch { }
}

Console.WriteLine("All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 A-E + V0.7 Journal/Recovery Analysis + V0.7.1 Durable Mutation/Recovery Orchestration + V0.7.2 Startup Discovery/Recovery Gate + V0.7.3 Startup Recovery Coordinator + V0.8 Transaction History/Durable Undo + V0.9 UI Execute/Undo Integration + V0.10 Release Candidate Retention smoke tests passed.");







sealed class FakeFileSystem : IReadOnlyFileSystem
{
    private readonly Dictionary<string, FileSystemEntryKind> _entries = new(StringComparer.OrdinalIgnoreCase);
    public bool BlockTemporaryNames { get; set; }

    public void SetExisting(params string[] paths)
    {
        _entries.Clear();
        foreach (var path in paths) _entries[path] = FileSystemEntryKind.File;
    }

    public void SetEntry(string path, FileSystemEntryKind kind)
    {
        _entries.Clear();
        _entries[path] = kind;
    }

    public FileSystemEntryKind GetEntryKind(string path)
    {
        if (BlockTemporaryNames && Path.GetFileName(path).StartsWith(".~br-", StringComparison.Ordinal))
            return FileSystemEntryKind.File;
        return _entries.TryGetValue(path, out var kind) ? kind : FileSystemEntryKind.Missing;
    }
}

sealed class FakeSemanticsProvider(bool caseSensitive) : IPathSemanticsProvider
{
    public PathSemantics GetSemantics(string directoryPath)
        => new(caseSensitive, true, 255, 32767, "SmokeTest");
}

sealed class FakeIdentityProvider : IFileIdentityProvider
{
    private readonly Dictionary<string, FileIdentity> _identities = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string path, FileIdentity identity) => _identities[path] = identity;

    public FileIdentity? TryGetIdentity(string path, bool isDirectory)
        => _identities.TryGetValue(path, out var identity) ? identity : null;
}


sealed class FailOnNthMutationFileSystem(IRenameMutationFileSystem inner, int failOnCall) : IRenameMutationFileSystem
{
    private int _calls;

    public void MoveFileNoOverwrite(string sourcePath, string temporaryPath)
    {
        ThrowIfInjected();
        inner.MoveFileNoOverwrite(sourcePath, temporaryPath);
    }

    public void MoveDirectoryNoOverwrite(string sourcePath, string temporaryPath)
    {
        ThrowIfInjected();
        inner.MoveDirectoryNoOverwrite(sourcePath, temporaryPath);
    }

    private void ThrowIfInjected()
    {
        _calls++;
        if (_calls == failOnCall)
            throw new IOException($"Injected namespace failure on mutation #{_calls}.");
    }
}


sealed class ApplyThenThrowOnNthMutationFileSystem(IRenameMutationFileSystem inner, int throwOnCall) : IRenameMutationFileSystem
{
    private int _calls;

    public void MoveFileNoOverwrite(string sourcePath, string temporaryPath)
    {
        _calls++;
        inner.MoveFileNoOverwrite(sourcePath, temporaryPath);
        if (_calls == throwOnCall)
            throw new IOException($"Injected post-apply namespace exception on mutation #{_calls}.");
    }

    public void MoveDirectoryNoOverwrite(string sourcePath, string temporaryPath)
    {
        _calls++;
        inner.MoveDirectoryNoOverwrite(sourcePath, temporaryPath);
        if (_calls == throwOnCall)
            throw new IOException($"Injected post-apply namespace exception on mutation #{_calls}.");
    }
}

sealed class FailOnNthJournalSink(ITransactionJournalSink inner, int failOnAppend) : ITransactionJournalSink
{
    private int _calls;

    public TransactionJournalAppendResult Append(string transactionDirectory, TransactionJournalEvent journalEvent)
    {
        _calls++;
        if (_calls == failOnAppend)
        {
            var journalPath = Path.Combine(Path.GetFullPath(transactionDirectory), TransactionJournal.JournalFileName);
            return new(
                false,
                journalPath,
                null,
                [new TransactionIssue(
                    ValidationSeverity.Error,
                    "INJECTED_JOURNAL_APPEND_FAILURE",
                    $"Injected durable journal failure on append #{_calls}.",
                    journalEvent.Ordinal,
                    journalEvent.ItemId,
                    journalPath)]);
        }

        return inner.Append(transactionDirectory, journalEvent);
    }
}
