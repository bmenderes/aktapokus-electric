using Ake.Core.Model;

namespace Ake.Core.Erc;

public enum Severity
{
    Error,
    Warning,
    Info,
}

/// <summary>Bir kuralın bulgusu. <see cref="Targets"/> ilgili nesnelerin kimlikleridir.</summary>
public sealed record ErcFinding(string RuleId, Severity Severity, string Message, IReadOnlyList<Guid> Targets);

/// <summary>Kurallara verilen bağlam. Bağlantı hesabı bir kez yapılır, bütün kurallar paylaşır.</summary>
public sealed record ErcContext(Project Project, Connectivity Connectivity);

/// <summary>
/// Deterministik bir mühendislik kuralı. Modeli değiştirmez; aynı model her zaman aynı bulguları
/// aynı sırada verir. Yeni kural eklemek bu arayüzü uygulayan bir sınıf yazmaktır.
/// </summary>
public interface IErcRule
{
    string Id { get; }

    IEnumerable<ErcFinding> Check(ErcContext context);
}

public sealed class ErcReport(IEnumerable<ErcFinding> findings)
{
    public IReadOnlyList<ErcFinding> Findings { get; } = [.. findings];

    public bool HasErrors => Findings.Any(finding => finding.Severity == Severity.Error);

    public int Count(Severity severity) => Findings.Count(finding => finding.Severity == severity);
}

public sealed class ErcEngine(IEnumerable<IErcRule> rules)
{
    private readonly List<IErcRule> _rules = [.. rules];

    /// <summary>Yerleşik kuralların tamamı.</summary>
    public static ErcEngine Default { get; } = new(
    [
        new OpenEndRule(),
        new ShortCircuitRule(),
        new DuplicateTagRule(),
        new DuplicateWireNumberRule(),
        new CrossSectionRule(),
        new IoUnassignedRule(),
        new IoDoubleAssignmentRule(),
        new OrphanRule(),
    ]);

    public IReadOnlyList<IErcRule> Rules => _rules;

    public ErcReport Run(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var context = new ErcContext(project, Connectivity.Compute(project));
        return new ErcReport(_rules.SelectMany(rule => rule.Check(context)));
    }
}
