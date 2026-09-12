using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using DeepDungeonTracker.Properties;
using System;
using System.Collections.Generic;

namespace DeepDungeonTracker;

public sealed class ResourceUI : IDisposable
{
    public IDalamudTextureWrap UI { get; }

    public IDalamudTextureWrap DeepDungeon { get; }

    public IDalamudTextureWrap Job { get; }

    public IDalamudTextureWrap Miscellaneous { get; }

    public IDalamudTextureWrap Coffer { get; }

    public IDalamudTextureWrap Enchantment { get; }

    public IDalamudTextureWrap Trap { get; }

    public IDalamudTextureWrap BossStatusTimer { get; }

    public IDalamudTextureWrap MapNormal { get; }

    public IDalamudTextureWrap MapHallOfFallacies { get; }

    public IFontHandle Axis { get; }

    public IFontHandle MiedingerMid { get; }

    public IFontHandle MiedingerMidLarge { get; }

    public IFontHandle TrumpGothic { get; }

    private readonly List<IDisposable> OwnedResources = [];

    public ResourceUI(IUiBuilder uiBuilder)
    {
        T Own<T>(T resource) where T : IDisposable
        {
            this.OwnedResources.Add(resource);
            return resource;
        }
        try
        {
            this.UI = Own(Service.TextureProvider.CreateFromImageAsync(Resources.UI).Result);
            this.DeepDungeon = Own(Service.TextureProvider.CreateFromImageAsync(Resources.DeepDungeon).Result);
            this.Job = Own(Service.TextureProvider.CreateFromImageAsync(Resources.Job).Result);
            this.Miscellaneous = Own(Service.TextureProvider.CreateFromImageAsync(Resources.Miscellaneous).Result);
            this.Coffer = Own(Service.TextureProvider.CreateFromImageAsync(Resources.Coffer).Result);
            this.Enchantment = Own(Service.TextureProvider.CreateFromImageAsync(Resources.Enchantment).Result);
            this.Trap = Own(Service.TextureProvider.CreateFromImageAsync(Resources.Trap).Result);
            this.BossStatusTimer = Own(Service.TextureProvider.CreateFromImageAsync(Resources.BossStatusTimer).Result);
            this.MapNormal = Own(Service.TextureProvider.CreateFromImageAsync(Resources.MapNormal).Result);
            this.MapHallOfFallacies = Own(Service.TextureProvider.CreateFromImageAsync(Resources.MapHallOfFallacies).Result);
            this.Axis = Own(ResourceUI.LoadFont(uiBuilder, GameFontFamily.Axis, 19.25f));
            this.MiedingerMid = Own(ResourceUI.LoadFont(uiBuilder, GameFontFamily.MiedingerMid, 16.0f));
            this.MiedingerMidLarge = Own(ResourceUI.LoadFont(uiBuilder, GameFontFamily.MiedingerMid, 22.0f));
            this.TrumpGothic = Own(ResourceUI.LoadFont(uiBuilder, GameFontFamily.TrumpGothic, 32.0f));
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var resource in this.OwnedResources)
        {
            try { resource.Dispose(); }
            catch (Exception e) { CaptureDiagnostics.Report("UI resource disposal failed", e); }
        }
        this.OwnedResources.Clear();
    }

    private static IFontHandle LoadFont(IUiBuilder uiBuilder, GameFontFamily family, float sizePx)
    {
        return uiBuilder.FontAtlas.NewDelegateFontHandle(x => x.OnPreBuild(toolkit =>
        {
            var fontPtr = toolkit.AddGameGlyphs(new(family, sizePx), null, null);
            toolkit.SetFontScaleMode(fontPtr, FontScaleMode.UndoGlobalScale);
        }));
    }
}