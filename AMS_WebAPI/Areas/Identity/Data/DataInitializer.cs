using AMS_WebAPI.Models;
using Microsoft.AspNetCore.Identity;

namespace AMS_WebAPI.Areas.Identity.Data
{
    public class DataInitializer
    {
        public static void SeedData(UserManager<AMS_WebAPIUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            SeedRoles(roleManager);
            SeedUsers(userManager);
        }

        public static void SeedUsers(UserManager<AMS_WebAPIUser> userManager)
        {
            string[] list = { "ALH", "AMO1", "AMO2", "AMO3", "BAL", "BOISE", "BON", "BOW", "BRU3", "BSA1", "BUL", "BWN", "CAR-1", "CAR-2", "CAR-3", "CCS1", "CCS2", "CLF12", "CLF34", "CLF56", "CLN", "COE", "CON1", "CON2", "CPS", "CRY12", "CRY45", "CVL4", "CVL56", "DAV4", "DKS", "EBD", "ELM1", "ELM2", "FLI", "GAV1", "GAV2", "GRE", "HAV6", "HEN-12", "HTC1", "HTC2", "HUGO", "INF", "JKS", "JOP", "KCR1", "KCR2", "KCR3", "KCR4", "KCR5", "KEYST-1", "KEYST-2", "KGC", "KLN", "KOD", "LAW", "LON", "LVP", "MABE", "MAEN", "MAES", "MIA7", "MIA8", "MIT1", "MIT2", "MLMR12", "MLR1", "MON-12", "MOR-12", "MOU", "MTN1", "MTN2", "MUS", "NOR-34", "OCR78", "ONT12", "PAR", "PAW", "PEA", "PKY", "PLS", "POLK", "PSGC", "PWW", "QUI", "RBD", "RHS", "ROS", "SANJ", "SCR", "SEM12", "SHR1", "SHR2", "SHR3", "SHR4", "SHW", "SMG", "SMI", "SPK", "SPR", "SPW", "STU-14", "SUD6", "SUN", "SWD", "THH-3", "TOLK1", "TRA1", "TRC", "TURK1", "VIC", "WAL", "WAR", "WGS4", "WLK", "WTR", "ZIM", "COL", "MLR2", "MLR3" };

            for (int i = 0; i < list.Length; i++)
            {
                if (userManager.FindByNameAsync(list[i]).Result == null)
                {
                    AMS_WebAPIUser user = new AMS_WebAPIUser();
                    user.UserName = list[i];
                    user.Email = $"{list[i]}@GMAIL.COM";
                    user.DBName = $"AMS_{list[i]}";

                    IdentityResult result = userManager.CreateAsync(user, $"{list[i]}Mistras!95").Result;
                    if (result.Succeeded)
                    {
                        userManager.AddToRoleAsync(user, UserRoles.DataSync_User).Wait();
                    }
                }
            }
        }

        public static void SeedRoles(RoleManager<IdentityRole> roleManager)
        {
            if (!roleManager.RoleExistsAsync(UserRoles.DataSync_User).Result)
            {
                IdentityRole role = new IdentityRole();
                role.Name = UserRoles.DataSync_User;
                IdentityResult roleResult = roleManager.CreateAsync(role).Result;
            }

            if (!roleManager.RoleExistsAsync(UserRoles.Web_User).Result)
            {
                IdentityRole role = new IdentityRole();
                role.Name = UserRoles.Web_User;
                IdentityResult roleResult = roleManager.CreateAsync(role).Result;
            }

            if (!roleManager.RoleExistsAsync(UserRoles.Web_Admin).Result)
            {
                IdentityRole role = new IdentityRole();
                role.Name = UserRoles.Web_Admin;
                IdentityResult roleResult = roleManager.CreateAsync(role).Result;
            }
        }
    }
}
