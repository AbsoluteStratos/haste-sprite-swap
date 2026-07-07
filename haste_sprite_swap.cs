using Landfall.Modding;

namespace haste_sprite_swap;

[LandfallPlugin]
public class haste_sprite_swap
{
    static haste_sprite_swap()
    {
        SpriteSwapService.Initialize();
    }
}
