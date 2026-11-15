namespace AyInisNavigatoru;

public enum AppLanguage { Turkish, English }

/// <summary>
/// İki dilli metin desteği (Türkçe / İngilizce).
/// Kullanım: L.T("Türkçe metin", "English text")
/// Dil değiştiğinde Changed olayı tetiklenir; arayüz kendini yeniden çizer.
/// Seçilen dil "dil.txt" dosyasına kaydedilir ve bir sonraki açılışta hatırlanır.
/// </summary>
public static class L
{
    private const string SettingsFile = "dil.txt";

    public static AppLanguage Current { get; private set; } = Load();

    public static bool En => Current == AppLanguage.English;

    public static event Action? Changed;

    public static string T(string tr, string en) => En ? en : tr;

    public static void Set(AppLanguage lang)
    {
        if (lang == Current) return;
        Current = lang;
        try { File.WriteAllText(SettingsFile, lang == AppLanguage.English ? "en" : "tr"); } catch { /* kaydedilemezse önemli değil */ }
        Changed?.Invoke();
    }

    public static void Toggle() => Set(En ? AppLanguage.Turkish : AppLanguage.English);

    private static AppLanguage Load()
    {
        try
        {
            if (File.Exists(SettingsFile) && File.ReadAllText(SettingsFile).Trim() == "en") return AppLanguage.English;
        }
        catch { /* yok say */ }
        return AppLanguage.Turkish;
    }
}
