using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DeepDungeonTracker;

public unsafe static partial class NodeUtility
{
    public record Node(float X, float Y, ushort Width, ushort Height, ushort PartId, uint PartCount);

    [GeneratedRegex("\\d+")]
    private static partial Regex NumberRegex();

    private static int Aetherpool(AtkUnitBase* addon, int index)
    {
        var componentNode = AsComponent(NodeUtility.GetAddonChildNode(addon, index));
        if (componentNode != null && componentNode->Component != null && componentNode->Component->UldManager.NodeList != null)
        {
            var manager = componentNode->Component->UldManager;
            for (var i = 0; i < manager.NodeListCount; i++)
            {
                var textNode = AsText(manager.NodeList[i]);
                if (textNode != null)
                {
                    var text = textNode->NodeText.ToString();
                    if (text != null)
                    {
                        if (int.TryParse(NodeUtility.NumberRegex().Match(text).Value, out var aetherpool))
                            return aetherpool;
                    }
                }
            }
        }
        return 0;
    }

    public static (bool, int, int) AetherpoolStatus(IGameGui gameGui)
    {
        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("DeepDungeonStatus", 1).Address!;
        return (addon != null) ? (true, NodeUtility.Aetherpool(addon, 73), NodeUtility.Aetherpool(addon, 72)) : (false, -1, -1);
    }

    private static AtkComponentNode* AsComponent(AtkResNode* node) => node == null ? null : node->GetAsAtkComponentNode();
    private static AtkTextNode* AsText(AtkResNode* node) => node == null ? null : node->GetAsAtkTextNode();

    private static AtkResNode* GetAddonChildNode(AtkUnitBase* addon, int index)
    {
        if (addon == null || addon->UldManager.NodeList == null)
            return null;
        return (index >= 0 && index < addon->UldManager.NodeListCount) ? addon->UldManager.NodeList[index] : null;
    }

    private static AtkResNode* GetComponentChildNode(AtkComponentNode* componentNode, int index)
    {
        if (componentNode == null || componentNode->Component == null || componentNode->Component->UldManager.NodeList == null)
            return null;
        return (index >= 0 && index < componentNode->Component->UldManager.NodeListCount) ? componentNode->Component->UldManager.NodeList[index] : null;
    }

    public static int SaveSlotNumber(IGameGui gameGui)
    {
        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("DeepDungeonSaveData", 1).Address!;
        if (addon == null || !addon->IsVisible)
            return 0;

        // Use the same save-list node as the legacy layout, checking its native type first.
        var node = NodeUtility.GetAddonChildNode(addon, 2);
        var container = AsComponent(node);
        if (container == null || container->Component == null ||
            (container->Component->ComponentFlags & 1) == 0)
            return 0;

        var componentType = container->Component->GetComponentType();
        if (componentType == ComponentType.List)
        {
            var list = node->GetAsAtkComponentList();
            if (list == null || list->ListLength != 2)
                return 0;
            // SelectedItemIndex differs from the hovered/held row and is -1 when no row is selected.
            return list->SelectedItemIndex is 0 or 1 ? list->SelectedItemIndex + 1 : 0;
        }

        // Keep the legacy highlight fallback only for a plain container. Do not reinterpret an unknown component or
        // override an explicit "no selection" from a typed list with its hover animation.
        if (componentType != ComponentType.Base)
            return 0;
        static AtkResNode* GetLegacySlotNode(AtkComponentNode* container, int index)
        {
            var row = AsComponent(NodeUtility.GetComponentChildNode(container, index));
            return NodeUtility.GetComponentChildNode(row, 1);
        }
        var slot1Node = GetLegacySlotNode(container, 1);
        var slot2Node = GetLegacySlotNode(container, 2);
        if (slot1Node == null || slot2Node == null || !slot1Node->IsVisible() || !slot2Node->IsVisible())
            return 0;
        return slot1Node->AddRed > slot2Node->AddRed ? 1 : slot2Node->AddRed > slot1Node->AddRed ? 2 : 0;
    }

    public static (bool, bool) SaveSlotDeletion(IGameGui gameGui)
    {
        static bool IsEmpty(AtkTextNode* node) => node->NodeText.ToString().IsNullOrWhitespace();

        static AtkTextNode* GetSlotNodeData(AtkUnitBase* addon, int index)
        {
            var componentNode = AsComponent(NodeUtility.GetAddonChildNode(addon, 2));
            componentNode = AsComponent(NodeUtility.GetComponentChildNode(componentNode, index));
            var textNode = AsText(NodeUtility.GetComponentChildNode(componentNode, 19));
            if (textNode != null && !IsEmpty(textNode))
            {
                var nextSiblingNode = textNode->AtkResNode.NextSiblingNode;
                if (nextSiblingNode != null)
                    return nextSiblingNode->GetAsAtkTextNode();
            }
            return null;
        }

        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("DeepDungeonSaveData", 1).Address!;
        if (addon == null || !addon->IsVisible)
            return (false, false);

        var slot1NodeData = GetSlotNodeData(addon, 1);
        var slot2NodeData = GetSlotNodeData(addon, 2);

        if (slot1NodeData != null && slot2NodeData != null)
            return (IsEmpty(slot1NodeData), IsEmpty(slot2NodeData));

        return (false, false);
    }

    public static (bool, int) MapFloorNumber(IGameGui gameGui)
    {
        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("DeepDungeonMap", 1).Address!;
        if (addon == null || addon->UldManager.NodeList == null)
            return (false, -1);

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var textNode = AsText(addon->UldManager.NodeList[i]);
            if (textNode == null)
                continue;

            if (int.TryParse(NodeUtility.NumberRegex().Match(textNode->NodeText.ToString()).Value, out var number))
                return (true, number);
            break;
        }
        return (false, -1);
    }

    public static IImmutableList<Node>? MapRoom(IGameGui gameGui)
    {
        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("DeepDungeonMap", 1).Address!;
        if (addon == null || addon->UldManager.NodeList == null)
            return null;

        IImmutableList<Node> nodes = ImmutableArray.Create<Node>();
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var resNode = addon->UldManager.NodeList[i];
            if (resNode == null)
                continue;

            var componentNode = resNode->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;

            var childNode = GetComponentChildNode(componentNode, 0);
            var imageNode = childNode == null ? null : childNode->GetAsAtkImageNode();
            if (imageNode == null || imageNode->PartsList == null || resNode->Width == 0 || resNode->Height == 0)
                continue;

            var x = resNode->X;
            var y = resNode->Y;
            if (x == 0.0f && y == 0.0f)
                continue;

            var id = imageNode->PartId;
            var count = imageNode->PartsList->PartCount;
            if ((id >= 0 && id < 16 && count == 16) || (id >= 0 && id < 9 && count == 9))
                nodes = nodes.Add(new(x, y, resNode->Width, resNode->Height, id, count));
        }
        return nodes;
    }

    public static bool CairnOfPassageActivation(IGameGui gameGui)
    {
        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("DeepDungeonMap", 1).Address!;
        if (addon == null || addon->UldManager.NodeList == null)
            return false;

        var skipFirst = false;
        for (var i = addon->UldManager.NodeListCount - 1; i >= 0; i--)
        {
            var resNode = addon->UldManager.NodeList[i];
            if (resNode == null)
                continue;

            var componentNode = resNode->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;

            var childNode = GetComponentChildNode(componentNode, 1);
            var imageNode = childNode == null ? null : childNode->GetAsAtkImageNode();
            if (imageNode == null || imageNode->PartsList == null)
                continue;

            if (imageNode->PartsList->PartCount == 11)
            {
                if (!skipFirst)
                {
                    skipFirst = true;
                    continue;
                }
                return imageNode->PartId == 10;
            }
        }
        return false;
    }

    private static (bool, int) ScoreWindowData(IGameGui gameGui, int index)
    {
        static (bool, int) GetValue(AtkComponentNode* node)
        {
            if (node == null || node->Component == null)
                return (false, -1);

            var buffer = string.Empty;
            for (var i = node->Component->UldManager.NodeListCount - 1; i >= 0; i--)
            {
                var resNode = AsComponent(GetComponentChildNode(node, i));
                if (resNode == null || resNode->Component == null)
                    continue;

                var childNode = GetComponentChildNode(resNode, 0);
                var imageNode = childNode == null ? null : childNode->GetAsAtkImageNode();
                if (imageNode == null)
                    continue;

                if (imageNode->PartId > 9)
                    return (false, -1);
                buffer += imageNode->PartId.ToString(CultureInfo.InvariantCulture);
            }
            return int.TryParse(buffer, out int value) ? (true, value) : (false, -1);
        }

        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("DeepDungeonResult", 1).Address!;
        if (addon == null)
            return (false, -1);

        var exitNode = GetAddonChildNode(addon, 2);
        var exitButton = exitNode == null ? null : exitNode->GetAsAtkComponentButton();
        if (exitButton == null || !exitButton->IsEnabled)
            return (false, -1);

        var floorNode = AsComponent(GetAddonChildNode(addon, 13));
        if (floorNode == null)
            return (false, -1);

        var result = GetValue(floorNode);
        if (!result.Item1 || result.Item2 <= 0)
            return (false, -1);

        var node = AsComponent(GetAddonChildNode(addon, index));
        if (node == null)
            return (false, -1);

        return GetValue(node);
    }

    public static (bool, int) ScoreWindowKills(IGameGui gameGui) => NodeUtility.ScoreWindowData(gameGui, 11);

    public static (bool, int) ScoreWindowScorePoints(IGameGui gameGui) => NodeUtility.ScoreWindowData(gameGui, 9);

    public static bool IsNowLoading(IGameGui gameGui)
    {
        var addon = (AtkUnitBase*)gameGui?.GetAddonByName("NowLoading", 1).Address!;
        return addon != null && addon->IsVisible;
    }
}
