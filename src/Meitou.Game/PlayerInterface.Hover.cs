using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    /// <summary>
    /// The mouse-over tooltip of an inventory item. Characters get none: the original shows no name or goal over a character under the cursor (they appear in the
    /// information panel after a click; the character is only outlined, <see cref="HoverTarget"/>).
    /// </summary>
    void AddHoverLabel(List<UiControl> controls)
    {
        if (inventoryDrag is not null || tradeDrag is not null) return;
        if (MyGuiPresentation.HitTest(controls, mouse.X, mouse.Y) is { } hit)
        {
            if (hit.Properties.TryGetValue("ItemTooltip", out string? tooltip))
            {
                // The item's name, then label and value rows (a tab between them) as the original's tooltip lists them.
                string[] lines = tooltip.Split('\n');
                float scale = InventoryScale, row = 20 * scale, width = Math.Min(Width, 280 * scale), height = 16 * scale + row * lines.Length;
                var box = new UiRect(Math.Clamp(mouse.X + 18 * scale, 0, Width - width), Math.Clamp(mouse.Y + 18 * scale, 0, Height - height), width, height);
                controls.Add(new("inventory-tooltip", "Widget", box, "", "", "Kenshi_GenericTextBoxSkin", true,
                    new Dictionary<string, string> { ["ItemTooltip"] = tooltip }, Vector4.Zero, Pickable: false));
                controls.Add(new("inventory-tooltip-title", "TextBox", new(box.X + 8 * scale, box.Y + 6 * scale, box.Width - 16 * scale, row), lines[0], "", "", true,
                    new Dictionary<string, string> { ["TextAlign"] = "Left Top", ["TextColour"] = TitleColour, ["WordWrap"] = "false" }, Vector4.Zero, Pickable: false));
                var host = new UiControl("inventory-tooltip-rows", "Widget", new(box.X + 4 * scale, box.Y + 6 * scale + row, box.Width - 8 * scale, row * (lines.Length - 1)), "", "", "", true,
                    new Dictionary<string, string>(), Vector4.Zero, Pickable: false);
                controls.Add(host);
                AddLines(controls, host.Name, lines.Skip(1).Select(TooltipRow).ToArray(), row);
            }
        }
    }
}
