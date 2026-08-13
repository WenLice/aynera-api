using Elaris.Domain.Audit.Statics;

namespace Elaris.Application.Tests;

public class AuditRedactionTests
{
    [Fact]
    public void MaskPhone_HidesMiddleDigits()
    {
        var masked = AuditRedaction.MaskPhone("+919876543210");
        Assert.DoesNotContain("987654", masked);
        Assert.EndsWith("3210", masked);
        Assert.StartsWith("+", masked);
    }

    [Fact]
    public void AuditChanges_OmitsUnchangedFields()
    {
        var changes = AuditChanges.Create(
        [
            ("name", "Delhi", "Delhi"),
            ("wave", 1, 2)
        ]);

        Assert.Single(changes);
        Assert.True(changes.ContainsKey("wave"));
        Assert.Contains("\"old\":1", AuditChanges.ToJson(changes)!, StringComparison.Ordinal);
    }
}
