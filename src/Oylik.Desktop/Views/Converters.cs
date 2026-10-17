using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Oylik.Desktop.Views;

public static class Converters
{
    /// <summary>true → qizil (manfiy qoldiq).</summary>
    public static readonly IValueConverter NegativeBrush =
        new FuncValueConverter<bool, IBrush>(negative => Palette.Get(negative ? "NegativeBrush" : "InkBrush"));

    /// <summary>Jadval katagi: Kunbay xodimda xira (jadval faqat «Jadval bo'yicha» uchun hisobga olinadi).</summary>
    public static readonly IValueConverter ScheduleOpacity =
        new FuncValueConverter<bool, double>(isSchedule => isSchedule ? 1.0 : 0.55);
}
