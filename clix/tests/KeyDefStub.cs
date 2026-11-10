// Stand-in for G-Helper's KeyDef (Peripherals/Keyboard/AuraKeyboardLayouts.cs), so Gk6xLayout compiles in the tests.
namespace GHelper.Peripherals.Keyboard
{
    public record struct KeyDef(string Name, float Width = 1f, float Gap = 0f, float Height = 1f);
}
