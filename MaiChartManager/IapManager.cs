using Windows.Services.Store;

namespace MaiChartManager;

public static class IapManager
{
    private const string storeId = "9NPJ5N4MMBR5";

# if !CRACK
    private static StoreContext StoreContext { get; } = StoreContext.GetDefault();
# endif

    private static Form _form;

    public enum LicenseStatus
    {
        Pending,
        Active,
        Inactive
    }

    public static LicenseStatus License { get; private set; } = LicenseStatus.Pending;

    public static async Task Init()
    {
# if CRACK
        License = LicenseStatus.Active;
        return;
# else
        if (!string.IsNullOrWhiteSpace(StaticSettings.Config.OfflineKey) && (await OfflineReg.VerifyAsync(StaticSettings.Config.OfflineKey)).IsValid)
        {
            License = LicenseStatus.Active;
            return;
        }

        var license = await StoreContext.GetAppLicenseAsync();
        if (license is null)
        {
            License = LicenseStatus.Inactive;
            return;
        }

        var item = license.AddOnLicenses.FirstOrDefault(x => x.Value.SkuStoreId.StartsWith(storeId));
        if (item.Value is null)
        {
            License = LicenseStatus.Inactive;
            return;
        }

        if (item.Value.IsActive)
        {
            License = LicenseStatus.Active;
        }
        else
        {
            License = LicenseStatus.Inactive;
        }
# endif
    }

    public static void BindToForm(Form form)
    {
        _form = form;
# if !CRACK
        WinRT.Interop.InitializeWithWindow.Initialize(StoreContext, form.Handle);
# endif
    }

    public static void SetOfflineLicenseActive()
    {
        License = LicenseStatus.Active;
    }

    public static async Task<StorePurchaseResult> Purchase()
    {
        if (_form.WindowState == FormWindowState.Minimized)
        {
            _form.WindowState = FormWindowState.Normal;
        }

        _form.Show();
        _form.Activate();
# if CRACK
        return null!;
# else
        var res = await StoreContext.RequestPurchaseAsync(storeId);
        if (res.Status == StorePurchaseStatus.Succeeded)
        {
            License = LicenseStatus.Active;
        }
        return res;
# endif
    }
}