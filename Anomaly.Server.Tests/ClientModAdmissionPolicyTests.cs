using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Server.Api.Auth;
using Anomaly.Server.Auth;
using Anomaly.Server.Utils;
using Anomaly.Shared.Auth;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ClientModAdmissionPolicyTests
{
    [Fact]
    public void MissingAnomalyClientCoreDeniesAdmission()
    {
        ClientModAdmissionPolicy.Configure(new Config(), new Version(1, 2, 3));
        try
        {
            AssertCoreRejection("missing", null, Inventory(("other.client", "1.0")));
        }
        finally
        {
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Theory]
    [InlineData("1.2.2")]
    [InlineData("1.2.4")]
    [InlineData("1.2.3.1")]
    [InlineData("1.2.3-beta")]
    public void IncorrectAnomalyClientCoreVersionDeniesAdmission(string presentVersion)
    {
        ClientModAdmissionPolicy.Configure(new Config(), new Version(1, 2, 3));
        try
        {
            AssertCoreRejection("wrong_version", presentVersion,
                Inventory((ClientModInventory.CoreId, presentVersion)));
        }
        finally
        {
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Fact]
    public void RegistrationEnforcesRangeAndStopsEnforcingAfterDispose()
    {
        ClientModAdmissionPolicy.Configure(new Config(), new Version(1, 2, 3));
        IDisposable registration = ClientModRequirements.Register("partner.client", "2.1", "3.0");
        try
        {
            AssertIssue("missing", "partner.client", Inventory(("anomaly.client", "1.2.3.0")));
            AssertIssue("wrong_version", "partner.client", Inventory(
                ("anomaly.client", "1.2.3"), ("partner.client", "2.0.9")));
            AssertIssue("wrong_version", "partner.client", Inventory(
                ("anomaly.client", "1.2.3"), ("partner.client", "3.0.0")));
            AssertIssue("wrong_version", "partner.client", Inventory(
                ("anomaly.client", "1.2.3"), ("partner.client", "2.1.0-beta")));
            Assert.Empty(ClientModAdmissionPolicy.Evaluate(Inventory(
                ("Anomaly.Client", "1.2.3.0"), ("Partner.Client", "2.5.0.0"))));
            Assert.Empty(ClientModAdmissionPolicy.Evaluate(Inventory(
                ("anomaly.client", "1.2.3"), ("partner.client", "2.1"))));
        }
        finally
        {
            registration.Dispose();
            registration.Dispose();
            ClientModAdmissionPolicy.Reset();
        }

        ClientModAdmissionPolicy.Configure(new Config(), new Version(1, 2, 3));
        try
        {
            Assert.Empty(ClientModAdmissionPolicy.Evaluate(Inventory(
                ("anomaly.client", "1.2.3"))));
        }
        finally
        {
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Fact]
    public void ExplicitBlocksOverrideRequirementsAndBlockAnyExemptsRequiredIds()
    {
        var config = new Config
        {
            BlockAnyAdditionalClientMods = true,
            BlockedClientModIds = new List<string> { "BLOCKED.Client", "PARTNER.CLIENT" }
        };
        ClientModAdmissionPolicy.Configure(config, new Version(1, 2, 3));
        IDisposable registration = ClientModRequirements.Register("partner.client", "2.0.0");
        IDisposable allowedRegistration = ClientModRequirements.Register("allowed.client", "1.0");
        try
        {
            var issues = ClientModAdmissionPolicy.Evaluate(Inventory(
                ("anomaly.client", "1.2.3"),
                ("allowed.client", "1.0"),
                ("blocked.client", "1.0.0"),
                ("partner.client", "1.0.0"),
                ("unrelated.client", "1.0.0")));

            Assert.Contains(issues, issue => issue.Code == "blocked" && issue.Id == "blocked.client");
            Assert.Contains(issues, issue => issue.Code == "blocked" && issue.Id == "partner.client");
            Assert.Contains(issues, issue => issue.Code == "unexpected" && issue.Id == "unrelated.client");
            Assert.DoesNotContain(issues, issue => issue.Id == "partner.client" && issue.Code != "blocked");
            Assert.Equal(3, issues.Count);
        }
        finally
        {
            allowedRegistration.Dispose();
            registration.Dispose();
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Fact]
    public void MultipleCompanionsHaveIndependentRegistrationHandles()
    {
        ClientModAdmissionPolicy.Configure(new Config(), new Version(1, 2, 3));
        IDisposable first = ClientModRequirements.Register("Partner Client", "1.0.0", "3.0.0");
        IDisposable second = ClientModRequirements.Register("partner client", "2.0.0");
        try
        {
            first.Dispose();
            AssertIssue("wrong_version", "partner client", Inventory(
                ("anomaly.client", "1.2.3"), ("Partner Client", "1.5.0")));
            Assert.Empty(ClientModAdmissionPolicy.Evaluate(Inventory(
                ("anomaly.client", "1.2.3"), ("Partner Client", "2.5.0"))));
        }
        finally
        {
            second.Dispose();
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Fact]
    public void InvalidPolicyBoundsFailBeforeRegistration()
    {
        Assert.Throws<ArgumentException>(() => ClientModRequirements.Register("partner.client", "1.0.0-beta"));
        Assert.Throws<ArgumentException>(() => ClientModRequirements.Register("partner.client", "1.0.0", "1.0.0"));
        Assert.Throws<ArgumentException>(() => ClientModRequirements.Register("partner\nclient", "1.0.0"));
        Assert.Throws<ArgumentException>(() => ClientModAdmissionPolicy.Configure(
            new Config { BlockedClientModIds = new List<string> { "invalid\n id" } },
            new Version(1, 2, 3)));
        Assert.Throws<ArgumentException>(() => ClientModAdmissionPolicy.Configure(
            new Config { BlockedClientModIds = null }, new Version(1, 2, 3)));
    }

    [Fact]
    public void ConflictingExplicitBlockWarnsOnceAndStillDenies()
    {
        var config = new Config
        {
            BlockedClientModIds = new List<string> { "PARTNER CLIENT" }
        };
        ClientModAdmissionPolicy.Configure(config, new Version(1, 2, 3));
        IDisposable registration = ClientModRequirements.Register("Partner Client", "1.0");
        try
        {
            var inventory = Inventory(("anomaly.client", "1.2.3"), ("partner client", "1.0"));
            AssertIssue("blocked", "Partner Client", inventory);
            AssertIssue("blocked", "Partner Client", inventory);
            Assert.Single(OffThreadLogger.Warnings.Where(message =>
                message.Contains("Client mod policy conflict:") &&
                message.Contains("Partner Client")));
        }
        finally
        {
            registration.Dispose();
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Fact]
    public void BlockingAnomalyCoreWarnsAndDenies()
    {
        ClientModAdmissionPolicy.Configure(new Config
        {
            BlockedClientModIds = new List<string> { "ANOMALY.CLIENT" }
        }, new Version(1, 2, 3));
        try
        {
            AssertIssue("blocked", "anomaly.client", Inventory(("anomaly.client", "1.2.3")));
            Assert.Single(OffThreadLogger.Warnings.Where(message =>
                message.Contains("Client mod policy conflict:") &&
                message.Contains("'anomaly.client' is required by Anomaly")));
        }
        finally
        {
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntegrityPolicyRejectsRiskByDefaultButNotOptInOrBlockedAttempts(bool optIn)
    {
        ClientModAdmissionPolicy.Configure(new Config(), new Version(1, 2, 3));
        try
        {
            Assert.False(ClientModAdmissionPolicy.AllowsIntegrity(null));
            Assert.True(ClientModAdmissionPolicy.AllowsIntegrity(new ClientIntegrityAudit(ClientIntegrityAuditFlags.None, optIn)));
            Assert.True(ClientModAdmissionPolicy.AllowsIntegrity(new ClientIntegrityAudit(ClientIntegrityAuditFlags.BlockedAttemptObserved, optIn)));
            foreach (var flag in new[] { ClientIntegrityAuditFlags.ExternalHarmonyModification,
                         ClientIntegrityAuditFlags.ExternalDetourModification, ClientIntegrityAuditFlags.CoverageIncomplete,
                         ClientIntegrityAuditFlags.InventoryDivergence })
                Assert.False(ClientModAdmissionPolicy.AllowsIntegrity(new ClientIntegrityAudit(flag, optIn)));
        }
        finally
        {
            ClientModAdmissionPolicy.Reset();
        }
    }

    [Fact]
    public void AllowingIntegrityRiskDoesNotWeakenExistingModRules()
    {
        ClientModAdmissionPolicy.Configure(new Config
        {
            AllowRiskyClientModifications = true,
            BlockAnyAdditionalClientMods = true,
            BlockedClientModIds = new List<string> { "blocked.mod" },
        }, new Version(1, 2, 3));
        using (ClientModRequirements.Register("required.mod", "1.0"))
        {
            try
            {
                Assert.True(ClientModAdmissionPolicy.AllowsIntegrity(new ClientIntegrityAudit(ClientIntegrityAudit.KnownFlags, true)));
                Assert.False(ClientModAdmissionPolicy.AllowsIntegrity(null));
                var issues = ClientModAdmissionPolicy.Evaluate(Inventory(
                    ("anomaly.client", "1.2.2"), ("blocked.mod", "1.0"), ("extra.mod", "1.0")));
                Assert.Contains(issues, issue => issue.Code == "wrong_version" && issue.Id == "anomaly.client");
                Assert.Contains(issues, issue => issue.Code == "missing" && issue.Id == "required.mod");
                Assert.Contains(issues, issue => issue.Code == "blocked" && issue.Id == "blocked.mod");
                Assert.Contains(issues, issue => issue.Code == "unexpected" && issue.Id == "extra.mod");
            }
            finally
            {
                ClientModAdmissionPolicy.Reset();
            }
        }
    }

    private static ClientModInventory Inventory(params (string Id, string Version)[] entries) =>
        new ClientModInventory(entries.Select(entry => new ClientModEntry(entry.Id, entry.Version)));

    private static void AssertCoreRejection(string code, string presentVersion, ClientModInventory inventory)
    {
        // A nonempty issue list takes the connection's Custom/RejectForce branch.
        var issue = Assert.Single(ClientModAdmissionPolicy.Evaluate(inventory));
        Assert.Equal(code, issue.Code);
        Assert.Equal(ClientModInventory.CoreId, issue.Id);
        Assert.Equal("1.2.3", issue.Required);
        Assert.Equal(presentVersion, issue.Present);

        string json = ModAdmissionResponseCodec.EncodeBounded(new[] { issue });
        Assert.True(ModAdmissionResponseCodec.TryDecode(json, out var response));
        Assert.Equal(0, response.OmittedCount);
        var transmittedIssue = Assert.Single(response.Issues);
        Assert.Equal(issue.Code, transmittedIssue.Code);
        Assert.Equal(issue.Id, transmittedIssue.Id);
        Assert.Equal(issue.Required, transmittedIssue.Required);
        Assert.Equal(issue.Present, transmittedIssue.Present);
    }

    private static void AssertIssue(string code, string id, ClientModInventory inventory) =>
        Assert.Contains(ClientModAdmissionPolicy.Evaluate(inventory),
            issue => issue.Code == code && StringComparer.OrdinalIgnoreCase.Equals(issue.Id, id));
}
