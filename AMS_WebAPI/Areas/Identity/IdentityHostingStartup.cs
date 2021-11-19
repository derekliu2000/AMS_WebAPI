using AMS_WebAPI.Areas.Identity.Data;
using AMS_WebAPI.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(AMS_WebAPI.Areas.Identity.IdentityHostingStartup))]
namespace AMS_WebAPI.Areas.Identity
{
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) => {
                services.AddDbContext<AMS_WebAPIContext>(options =>
                    options.UseSqlServer(
                        context.Configuration.GetConnectionString("AMS_WebAPIContextConnection")));

                services.AddIdentity<AMS_WebAPIUser, IdentityRole>()
                        .AddEntityFrameworkStores<AMS_WebAPIContext>()
                        .AddDefaultTokenProviders();
            });
        }
    }
}