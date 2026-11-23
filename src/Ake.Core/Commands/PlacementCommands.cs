using System.Globalization;
using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Bir yerleşimi taşır ve/veya döndürür. Yalnızca grafik temsili değiştirir; elektriksel model
/// (bağlantılar, potansiyeller) aynı kalır.
/// </summary>
public sealed class MovePlacement(PlacementId placementId, double x, double y, int rotationDeg) : ICommand
{
    private Placement? _placement;
    private (double X, double Y, int Rotation) _previous;

    public string Description => string.Create(
        CultureInfo.InvariantCulture, $"Sembol taşındı: ({x:0.##}, {y:0.##}) mm, {rotationDeg}°");

    public Result Execute(Project project)
    {
        var placement = project.Placements.FirstOrDefault(candidate => candidate.Id == placementId);
        if (placement is null)
        {
            return Result.Failure(new Error("placement.yok", $"Yerleşim modelde yok ({placementId})."));
        }

        if (rotationDeg is not (0 or 90 or 180 or 270))
        {
            return Result.Failure(new Error(
                "placement.donus_gecersiz", $"Yerleşim dönüşü 0, 90, 180 ya da 270 olmalı; verilen: {rotationDeg}"));
        }

        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return Result.Failure(new Error("placement.konum_gecersiz", "Yerleşim konumu sonlu bir sayı olmalı."));
        }

        _placement = placement;
        _previous = (placement.X, placement.Y, placement.RotationDeg);
        placement.X = x;
        placement.Y = y;
        placement.RotationDeg = rotationDeg;
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _placement!.X = _previous.X;
        _placement.Y = _previous.Y;
        _placement.RotationDeg = _previous.Rotation;
    }
}

/// <summary>
/// Yerleşimi kaldırır: function sayfadan kalkar, elektriksel model (cihaz, bağlantılar) aynı kalır.
/// Geri alınınca aynı sıraya döner.
/// </summary>
public sealed class RemovePlacement(PlacementId placementId) : ICommand
{
    private Placement? _placement;
    private int _index;

    public string Description => "Sembol sayfadan kaldırıldı";

    public Result Execute(Project project)
    {
        _index = project.PlacementList.FindIndex(candidate => candidate.Id == placementId);
        if (_index < 0)
        {
            return Result.Failure(new Error("placement.yok", $"Yerleşim modelde yok ({placementId})."));
        }

        _placement = project.PlacementList[_index];
        project.PlacementList.RemoveAt(_index);
        return Result.Success();
    }

    public void Undo(Project project) => project.PlacementList.Insert(_index, _placement!);
}

/// <summary>Yerleşimin görünüm rolünü (Ana / Referans) değiştirir.</summary>
public sealed class SetPlacementRole(PlacementId placementId, PlacementRole role) : ICommand
{
    private Placement? _placement;
    private PlacementRole _previous;

    public string Description => role == PlacementRole.Main ? "Ana fonksiyon olarak işaretlendi" : "Referans görünüm olarak işaretlendi";

    public Result Execute(Project project)
    {
        var placement = project.Placements.FirstOrDefault(candidate => candidate.Id == placementId);
        if (placement is null)
        {
            return Result.Failure(new Error("placement.yok", $"Yerleşim modelde yok ({placementId})."));
        }

        if (role == PlacementRole.Main
            && project.Placements.Any(p => p.Id != placementId && p.FunctionId == placement.FunctionId && p.Role == PlacementRole.Main))
        {
            return Result.Failure(new Error(
                "placement.ana_tekrar", $"Function'ın zaten bir ana görünümü var ({placement.FunctionId})."));
        }

        _placement = placement;
        _previous = placement.Role;
        placement.Role = role;
        return Result.Success();
    }

    public void Undo(Project project) => _placement!.Role = _previous;
}
