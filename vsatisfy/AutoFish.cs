using FFXIVClientStructs.FFXIV.Client.Game;
﻿using clib.TaskSystem;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace Satisfy;

// execute full fishing delivery: teleport to zone, fish, turn in
// TODO: automate actual fishing, use autohook
public sealed class AutoFish(NPCInfo npc) : AutoCommon
{
    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(2);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.FishData == null || npc.CraftData == null)
            throw new Exception("钓鱼或交付数据未初始化");

        var turnInItemId = npc.FishData.FishItemId;
        var remainingFish = remainingTurnins - InventoryManager.GetItemCount(turnInItemId, 1);
        if (remainingFish > 0)
        {
            Status = "前往渔点";
            await TeleportTo(npc.FishData.TerritoryTypeId, npc.FishData.Center);

            // TODO: improve move-to destination (ideally closest point where you can actually fish...)
            if (npc.FishData.IsSpearFish)
                Status = $"在 {Service.LuminaRow<SpearfishingNotebook>(npc.FishData.FishSpotId)?.PlaceName.ValueNullable?.Name} 刺鱼";
            else
                Status = $"在 {Service.LuminaRow<FishingSpot>(npc.FishData.FishSpotId)?.PlaceName.ValueNullable?.Name} 钓鱼";
            await MoveTo(npc.FishData.Center, MovementConfig.Everything.WithTolerance(10));
        }
        else // TODO: full auto...
        {
            Status = "前往交付地";
            await TeleportTo(npc.TerritoryId, npc.CraftData.TurnInLocation);

            Status = $"正在交付 {remainingTurnins}x {ItemName(turnInItemId)}";
            await MoveTo(npc.CraftData.TurnInLocation, MovementConfig.InteractRange);
            await TurnIn(npc, 2);
        }
    }
}
