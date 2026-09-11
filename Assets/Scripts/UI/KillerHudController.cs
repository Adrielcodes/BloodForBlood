using UnityEngine;
using UnityEngine.UI;

// Built entirely at runtime (no prefab asset) — same reasoning as SurvivorHudController. Only
// ever created for the locally owned Killer (see KillerController.OnNetworkSpawn).
public class KillerHudController : MonoBehaviour
{
    private KillerController killer;
    private Image healthFill;

    public static KillerHudController Create(KillerController target)
    {
        var canvasGO = new GameObject("KillerHUD");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        KillerHudController hud = canvasGO.AddComponent<KillerHudController>();
        hud.healthFill = HudBar.Create(canvasGO.transform, "Health", new Vector2(20f, 30f), new Color(0.8f, 0.15f, 0.15f));
        hud.Bind(target);

        return hud;
    }

    private void Bind(KillerController target)
    {
        killer = target;
        killer.Health.OnValueChanged += (previous, current) => UpdateHealth();
        UpdateHealth();
    }

    private void UpdateHealth()
    {
        if (healthFill == null || killer == null || killer.MaxHealth <= 0) return;
        healthFill.fillAmount = (float)killer.Health.Value / killer.MaxHealth;
    }
}
