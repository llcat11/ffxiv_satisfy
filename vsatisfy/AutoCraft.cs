using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace Satisfy;

// execute full crafting delivery: teleport to zone, buy ingredients if needed, craft if needed, turn in
public sealed class AutoCraft(NPCInfo npc) : AutoCommon
{
    private readonly ICallGateSubscriber<ushort, int, object> _artisanCraft = Service.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    private readonly ICallGateSubscriber<bool> _artisanInProgress = Service.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");

    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(0);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.CraftData == null)
            throw new Exception("制作数据未初始化");

        var turnInItemId = npc.TurnInItems[0];
        var remainingCrafts = remainingTurnins - InventoryManager.GetItemCount(turnInItemId, 1);
        if (remainingCrafts > 0)
        {
            var ingredient = CraftTurnin.GetCraftIngredient(turnInItemId);
            var requiredIngredients = ingredient.count * remainingCrafts;
            var missingIngredients = requiredIngredients - InventoryManager.GetItemCount(ingredient.id, 0);
            if (missingIngredients > 0)
            {
                Status = $"购买素材 {missingIngredients}x {ItemName(ingredient.id)}";
                if (Service.ClientState.TerritoryType == npc.TerritoryId)
                    await MoveTo(npc.CraftData.VendorLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernet: false);
                else
                    await MoveTo(npc.TerritoryId, npc.CraftData.VendorLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernetWithinTerritory: false);
                await Dismount();
                await BuyFromShop(npc.CraftData.VendorInstanceId, npc.CraftData.VendorShopId, ingredient.id, missingIngredients);
            }
            Status = $"制作 {remainingCrafts}x {ItemName(turnInItemId)}";
            await CraftItem(turnInItemId, remainingCrafts, remainingTurnins);
        }

        Status = $"正在交付 {remainingTurnins}x {ItemName(turnInItemId)}";
        if (Service.ClientState.TerritoryType == npc.TerritoryId)
            await MoveTo(npc.CraftData.TurnInLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernet: false);
        else
            await MoveTo(npc.TerritoryId, npc.CraftData.TurnInLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernetWithinTerritory: false);
        await TurnIn(npc, 0);
    }

    private async Task BuyFromShop(ulong vendorInstanceId, uint shopId, uint itemId, int count)
    {
        using var scope = BeginScope("Buy");
        if (!AgentShop.IsShopOpen(shopId))
        {
            Log("正在打开商店...");
            ErrorIf(!AgentShop.OpenShop(vendorInstanceId, shopId), $"无法打开商店 {vendorInstanceId:X}.{shopId:X}");
            await WaitWhile(() => !AgentShop.IsShopOpen(shopId), "WaitForOpen");
            await WaitWhile(() => !Service.Conditions[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        }

        Log("正在购买...");
        ErrorIf(!AgentShop.BuyItemFromShop(shopId, itemId, count), $"无法从商店 {vendorInstanceId:X}.{shopId:X} 购买 {count}x {itemId}");
        await WaitWhile(() => AgentShop.ShopTransactionInProgress(shopId), "Transaction");
        Log("正在关闭商店...");
        ErrorIf(!AgentShop.CloseShop(), $"无法关闭商店 {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => AgentShop.IsShopOpen(), "WaitForClose");
        await WaitWhile(() => Service.Conditions[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        await NextFrame();
    }

    private async Task CraftItem(uint itemId, int count, int finalCount)
    {
        using var scope = BeginScope("Craft");
        if (CraftTurnin.GetRecipe(itemId) is { RowId: var rowId } && rowId != 0)
        {

            ArtisanCraft((ushort)rowId, count);
            await WaitWhile(() => !ArtisanInProgress(), "WaitStart");
            await WaitWhile(ArtisanInProgress, "WaitProgress");
            await WaitWhile(() => !Service.Conditions[ConditionFlag.PreparingToCraft], "WaitFinish");
            ErrorIf(InventoryManager.GetItemCount(itemId, 1) < finalCount, $"制作 {count}x {ItemName(itemId)} 失败");
            await NextFrame();

            AgentRecipeNote.Close();
            await WaitWhile(() => Service.Conditions[ConditionFlag.Crafting], "WaitCraftClose");
        }
        else
            Error($"找不到物品 {itemId} 的配方");
    }

    private void ArtisanCraft(ushort recipe, int count) => _artisanCraft.InvokeAction(recipe, count);
    private bool ArtisanInProgress() => _artisanInProgress.InvokeFunc();

}
