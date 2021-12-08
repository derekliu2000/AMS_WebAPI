using AMS_WebAPI.Models;
using API.Common.Utils;
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
            string[] list = { "AMO1", "AMO2", "AMO3", "BAL", "BON", "BSA1", "BUL", "BWN", "CCS1", "CCS2", "CLN", "COE", "COL", "CON1", "CON2", "DAV4", "DKS", "EBD", "ELM1", "ELM2", "FLI", "GAV1", "GAV2", "GRE", "HTC1", "HTC2", "HUGO", "INF", "JKS", "JOP", "KCR1", "KCR2", "KCR3", "KCR4", "KCR5", "KEYST-1", "KEYST-2", "KGC", "KOD", "LAW", "LON", "LVP", "MABE", "MAEN", "MIA7", "MIA8", "MIT1", "MIT2", "MLMR12", "MLR1", "MLR2", "MLR3", "MLR4", "MON-12", "MOR-12", "MOU", "MUS", "NOR-34", "OCR78", "PAW", "PEA", "PKY", "PLS", "PSGC", "PWW", "QUI", "RHS", "SANJ", "SCR", "SEM12", "SHR1", "SHR2", "SHR3", "SMG", "SMI", "SPK", "SPR", "SPW", "SWD", "TRC", "TURK1", "VIC", "WAL", "WAR", "WGS4", "ZIM" };

            for (int i = 0; i < list.Length; i++)
            {
                if (userManager.FindByNameAsync(list[i]).Result == null)
                {
                    AMS_WebAPIUser user = new AMS_WebAPIUser();
                    user.UserName = "AMS_" + list[i];
                    user.Email = $"{list[i]}@GMAIL.COM";
                    user.DBName = $"AMS_{list[i]}";
                    user.DBUID = Rijndael.Encrypt("data_sync_AMS_" + list[i], Rijndael.EP_PASSPHRASE, Rijndael.EP_SALTVALUE, Rijndael.EP_HASHALGORITHM, Rijndael.EP_PASSWORDITERATIONS, Rijndael.EP_INITVECTOR, Rijndael.EP_KEYSIZE);
                    user.DBPassword = Rijndael.Encrypt("AMS_" + list[i] + "Mistras$95", Rijndael.EP_PASSPHRASE, Rijndael.EP_SALTVALUE, Rijndael.EP_HASHALGORITHM, Rijndael.EP_PASSWORDITERATIONS, Rijndael.EP_INITVECTOR, Rijndael.EP_KEYSIZE);

                    IdentityResult result = userManager.CreateAsync(user, $"AMS_{list[i]}Mistras!95").Result;
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
