using Anomaly.ReleasePackaging;

if (args.Length == 0)
    return Usage();

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "create-local" when args.Length == 6:
            ReleasePackageBuilder.CreateLocalReleaseArchives(args[1], args[2], args[3], args[4], args[5]);
            return 0;
        case "create-runtime" when args.Length == 6:
            ReleasePackageBuilder.CreateLocalReleaseArchives(args[1], args[2], args[3], args[4], args[5], includeSdk: false);
            return 0;
        case "validate-skins" when args.Length == 2:
            ReleasePackageBuilder.ValidateSkinPack(args[1]);
            return 0;
        case "create" when args.Length == 7:
            ReleasePackageBuilder.Create(
                args[1], args[2], args[3], args[4], args[5], args[6]);
            return 0;
        case "verify" when args.Length == 2:
            ReleasePackageBuilder.Verify(args[1]);
            return 0;
        case "audit-closure" when args.Length == 2:
            ReleasePackageBuilder.AuditRuntimeClosures(args[1]);
            return 0;
        case "finalize-gate-evidence" when args.Length == 3:
            ReleasePackageBuilder.FinalizeGateEvidence(args[1], args[2]);
            return 0;
        case "record-signing" when args.Length == 4:
            ReleasePackageBuilder.RecordSigning(args[1], args[2], args[3]);
            return 0;
        default:
            return Usage();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  create-local <repository-root> <output-directory> <version> <resolved-reference-directory> <skin-index-path>");
    Console.Error.WriteLine("  create-runtime <repository-root> <output-directory> <version> <resolved-reference-directory> <skin-index-path>");
    Console.Error.WriteLine("  validate-skins <skin-index-path>");
    Console.Error.WriteLine("  create <repository-root> <output-directory> <version> <source-commit> <source-date-epoch> <resolved-reference-directory>");
    Console.Error.WriteLine("  verify <output-directory>");
    Console.Error.WriteLine("  audit-closure <output-directory>");
    Console.Error.WriteLine("  finalize-gate-evidence <output-directory> <gate-summary-json>");
    Console.Error.WriteLine("  record-signing <output-directory> <key-id> <public-key-pem>");
    return 2;
}
