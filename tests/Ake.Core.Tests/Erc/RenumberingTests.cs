using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Core.Tests.Commands;
using Ake.Core.Tests.Model;

namespace Ake.Core.Tests.Erc;

public class RenumberingTests
{
    [Fact]
    public void Tags_are_packed_per_letter_prefix_in_model_order()
    {
        var project = Build.Project(
        [
            Build.Device("K5", "A1"),
            Build.Device("F2", "1"),
            Build.Device("K9", "A1"),
            Build.Device("PE", "1"),
            Build.Device("K12", "A1"),
        ]);

        CommandAssert.RoundTrips(project, Renumbering.Tags(project));

        Assert.Equal(["K1", "F1", "K2", "PE", "K3"], project.Devices.Select(device => device.ProductTag));
    }

    [Fact]
    public void Tags_already_in_order_produce_no_changes()
    {
        var project = Build.Project([Build.Device("K1", "A1"), Build.Device("K2", "A1"), Build.Device("F1", "1")]);

        Assert.Empty(Renumbering.Tags(project).Commands);
    }

    [Fact]
    public void Tags_are_numbered_separately_under_each_structure_node()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, Renumbering.Tags(project));

        Assert.Equal("=M2+P1-K1", project.DesignationOf(project.Devices.Single(d => d.Description == "Motor kontaktörü 2")).ToString());
        Assert.Equal("=C1+P1-A2", project.DesignationOf(project.Devices.Single(d => d.Description == "EtherCAT coupler")).ToString());
        Assert.DoesNotContain(ErcEngine.Default.Run(project).Findings, finding => finding.RuleId == "erc.tag_tekrar");
    }

    [Fact]
    public void Sequential_wire_numbers_are_unique_and_the_reference_machine_already_has_them()
    {
        var project = TestData.RefMachine();

        Assert.Empty(Renumbering.Wires(project, WireNumberingScheme.Sequential).Commands);

        var numbers = project.Connections.Where(c => c.Wire is not null).Select(c => c.Wire!.Number).ToList();
        Assert.Equal(Enumerable.Range(1, 56).Select(n => n.ToString()), numbers);
    }

    [Fact]
    public void Per_potential_numbering_gives_one_number_to_each_potential()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, Renumbering.Wires(project, WireNumberingScheme.PerPotential));

        var connectivity = Connectivity.Compute(project);
        var wired = project.Connections.Where(connection => connection.Wire is not null).ToList();
        var numbersPerPotential = wired
            .GroupBy(connection => connectivity.PotentialOf(connection.From), ReferenceEqualityComparer.Instance)
            .Select(group => group.Select(connection => connection.Wire!.Number).Distinct().Count());

        Assert.All(numbersPerPotential, count => Assert.Equal(1, count));
        Assert.Equal(
            project.Connection("T1", "+", "A1", "24V").Wire!.Number,
            project.Connection("T1", "+", "K0", "33").Wire!.Number);
        Assert.Empty(ErcEngine.Default.Run(project).Findings);
    }

    [Fact]
    public void Renumbering_keeps_wire_identity_cross_section_and_colour()
    {
        var project = TestData.RefMachine();
        var connection = project.Connection("T1", "-", "K2", "A2");
        var before = connection.Wire!;

        new CommandStack(project).Execute(
            Renumbering.Wires(project, WireNumberingScheme.PerPotential), ChangeSource.User, "test");

        Assert.Equal(before.Id, connection.Wire!.Id);
        Assert.Equal(before.CrossSectionMm2, connection.Wire.CrossSectionMm2);
        Assert.Equal(before.Color, connection.Wire.Color);
        Assert.NotEqual(before.Number, connection.Wire.Number);
    }

    [Fact]
    public void Cable_cores_are_not_numbered()
    {
        var project = TestData.RefMachine();

        new CommandStack(project).Execute(
            Renumbering.Wires(project, WireNumberingScheme.PerPotential), ChangeSource.User, "test");

        Assert.Equal(8, project.Connections.Count(connection => connection.CoreId is not null && connection.Wire is null));
    }
}
