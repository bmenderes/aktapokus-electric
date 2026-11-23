using System.Globalization;
using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Kavşak noktası (ADR-0032): telin ortasında iki ya da daha çok telin buluştuğu nokta. Model olarak tek bağlantı noktalı,
/// <see cref="DeviceKind.Junction"/> türünde bir device'tır; sayfada küçük dolu bir nokta olarak yerleştirilir. Böylece her
/// tel bir uca bağlanır, aynı noktaya bağlı teller aynı net ve potansiyeli paylaşır ve mevcut komutlar (bağlantı, silme,
/// taşıma, geri alma) değişmeden çalışır.
/// </summary>
public static class Junctions
{
    /// <summary>Yeni kavşak: cihaz ve yerleşim, tek adımda. Dönen nokta, ona tel bağlanacak bağlantı noktasıdır.</summary>
    public static (Transaction Command, ConnectionPointId Point) Create(Project project, PageId page, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (device, point) = NewDevice(project);
        var placement = new Placement(PlacementId.New(), page, device.Functions[0].Id, PlacementRole.Main, x, y);
        return (new Transaction($"Kavşak eklendi: -{device.ProductTag}", [new AddDevice(device), new AddPlacement(placement)]), point);
    }

    /// <summary>
    /// Bağlantıyı verilen noktada ikiye böler: eski tel <c>baş – kavşak</c> olarak kalır, <c>kavşak – son</c> için aynı
    /// numara, kesit ve renkte yeni tel açılır. Köşe noktaları atılır (iki yarı yeniden otomatik yönlendirilir). Tek adımda
    /// geri alınır. Kablo damarına atanmış bağlantı bölünmez: damar iki bağlantıya atanamaz.
    /// </summary>
    public static Result<(Transaction Command, ConnectionPointId Point)> Split(
        Project project, ConnectionId connectionId, PageId page, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.FindConnection(connectionId) is not { } connection)
        {
            return Result.Failure<(Transaction, ConnectionPointId)>(
                new Error("connection.yok", $"Bağlantı modelde yok ({connectionId})."));
        }

        if (connection.CoreId is not null)
        {
            return Result.Failure<(Transaction, ConnectionPointId)>(new Error(
                "kavsak.damar", "Kablo damarına atanmış bağlantı kavşakla bölünemez; kablo damarı tek bir bağlantıya atanır."));
        }

        var (device, point) = NewDevice(project);
        var placement = new Placement(PlacementId.New(), page, device.Functions[0].Id, PlacementRole.Main, x, y);
        var second = connection.Wire is { } wire
            ? new Wire(WireId.New(), wire.Number, wire.CrossSectionMm2, wire.Color)
            : null;
        var command = new Transaction(
            $"Tel kavşakla bölündü: -{device.ProductTag}",
            [
                new Disconnect(connection.Id),
                new AddDevice(device),
                new AddPlacement(placement),
                new Connect(new Connection(ConnectionId.New(), connection.From, point, connection.Wire, null, null, connection.KindId)),
                new Connect(new Connection(ConnectionId.New(), point, connection.To, second, null, null, connection.KindId)),
            ]);
        return Result.Success((command, point));
    }

    // Kavşağın tag'i J1, J2…: kullanılmayan en küçük numara.
    private static (Device Device, ConnectionPointId Point) NewDevice(Project project)
    {
        var used = project.Devices.Select(device => device.ProductTag).ToHashSet(StringComparer.Ordinal);
        var number = 1;
        while (used.Contains("J" + number.ToString(CultureInfo.InvariantCulture)))
        {
            number++;
        }

        var point = new ConnectionPoint(ConnectionPointId.New(), "1");
        var function = new Function(FunctionId.New(), FunctionKind.Junction, [point]);
        var device = new Device(DeviceId.New(), "J" + number.ToString(CultureInfo.InvariantCulture), [function], DeviceKind.Junction);
        return (device, point.Id);
    }
}
