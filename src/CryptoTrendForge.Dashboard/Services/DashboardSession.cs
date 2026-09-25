namespace CryptoTrendForge.Dashboard.Services;

public sealed class DashboardSession
{
    public bool IsAuthenticated { get; private set; }

    public void SignIn()
    {
        IsAuthenticated = true;
    }

    public void SignOut()
    {
        IsAuthenticated = false;
    }
}
