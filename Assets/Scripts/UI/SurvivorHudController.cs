using UnityEngine;
using UnityEngine.UI;

// Built entirely at runtime (no prefab asset) — same reasoning as ThirdPersonCameraFollow: a
// prefab reference would need an AssetDatabase lookup, which is editor-only and unavailable at
// runtime. Only ever created for the locally owned Survivor (see SurvivorController.OnNetworkSpawn).
public class SurvivorHudController : MonoBehaviour
{
    private SurvivorController survivor;
    private Image healthFill;
    private Image staminaFill;

    public static SurvivorHudController Create(SurvivorController target)
    {
        var canvasGO = new GameObject("SurvivorHUD");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        SurvivorHudController hud = canvasGO.AddComponent<SurvivorHudController>();
        hud.healthFill = HudBar.Create(canvasGO.transform, "Health", new Vector2(20f, 60f), new Color(0.8f, 0.15f, 0.15f));
        hud.staminaFill = HudBar.Create(canvasGO.transform, "Stamina", new Vector2(20f, 30f), new Color(0.85f, 0.75f, 0.2f));
        hud.Bind(target);

        return hud;
    }

    private void Bind(SurvivorController target)
    {
        survivor = target;
        survivor.Stamina.OnValueChanged += (previous, current) => UpdateStamina();
        survivor.HitsTaken.OnValueChanged += (previous, current) => UpdateHealth();
        UpdateStamina();
        UpdateHealth();
    }

    private void UpdateStamina()
    {
        if (staminaFill == null || survivor == null || survivor.MaxStamina <= 0f) return;
        staminaFill.fillAmount = survivor.Stamina.Value / survivor.MaxStamina;
    }

    private void UpdateHealth()
    {
        if (healthFill == null || survivor == null || survivor.HitsToDown <= 0) return;
        int remaining = Mathf.Max(0, survivor.HitsToDown - survivor.HitsTaken.Value);
        healthFill.fillAmount = (float)remaining / survivor.HitsToDown;
    }
}
