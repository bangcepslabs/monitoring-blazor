namespace Monitoring.Blazor.Models;
    
public class IpApiResult    
{
    public string Ip
    {
        get => Query;
        set => Query = value;
    }

    public String Query { get; set; } = string.Empty;
    public String Status { get; set; } = string.Empty;
    public String Country { get; set; } = string.Empty;
    public String CountryCode { get; set; } = string.Empty;
    public String Region { get; set; } = string.Empty;
    public String RegionName { get; set; } = string.Empty;
    public String City { get; set; } = string.Empty;
    public String Zip { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public String Timezone { get; set; } = string.Empty;
    public String Isp { get; set; } = string.Empty;
    public String Org { get; set; } = string.Empty;
    public String As { get; set; } = string.Empty;
}
