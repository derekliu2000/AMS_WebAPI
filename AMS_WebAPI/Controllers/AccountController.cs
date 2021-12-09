using AMS_WebAPI.Areas.Identity.Data;
using AMS_WebAPI.Data;
using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.Utils;
using API.Common.WebAPI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private AMS_WebAPIContext _dbContext;
        private readonly UserManager<AMS_WebAPIUser> _userManager;
        private readonly SignInManager<AMS_WebAPIUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(AMS_WebAPIContext dbContext,
                                 UserManager<AMS_WebAPIUser> userManager,
                                 SignInManager<AMS_WebAPIUser> signInManager,
                                 RoleManager<IdentityRole> roleManager,
                                 IConfiguration configuration,
                                 ILogger<AccountController> logger)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("registerDataSyncUser")]
        public async Task<IActionResult> RegisterDataSyncUser([FromBody] RegisterModel model)
        {
            var userExist = await _userManager.FindByNameAsync(model.UserName);
            if (userExist != null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new Response { Status = RESULT.ERROR, Msg = "User already exist" });
            }

            AMS_WebAPIUser user = new AMS_WebAPIUser()
            {
                Email = model.Email,
                SecurityStamp = Guid.NewGuid().ToString(),
                UserName = model.UserName
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new Response { Status = RESULT.ERROR, Msg = "User creation fail" });
            }

            if (!await _roleManager.RoleExistsAsync(UserRoles.DataSync_User))
            {
                await _roleManager.CreateAsync(new IdentityRole(UserRoles.DataSync_User));
            }

            if (await _roleManager.RoleExistsAsync(UserRoles.DataSync_User))
            {
                await _userManager.AddToRoleAsync(user, UserRoles.DataSync_User);
            }

            return Ok(new Response { Status = RESULT.SUCCESS, Msg = "Data Sync User created successfully" });
        }

        [HttpPost]
        [Route("Login")]
        [AllowAnonymous]
        public async Task<IActionResult> PostLogin()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return Login(zippedData);
            }
        }

        private IActionResult Login(string value)
        {
            Response rsp;
            Buffer_Account_Login loginBuffer = null;

            try
            {
                loginBuffer = new Buffer_Account_Login(value, Request.Headers["Data-Hash"]);
            }
            catch (Exception e)
            {
                if (loginBuffer != null && loginBuffer.DBName != "")
                    rsp = new Response(RESULT.NEW_BUFFER, loginBuffer.DBName, Request.Headers["Host"], e.Message);
                else
                    rsp = new Response(RESULT.NEW_BUFFER, "", Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            AMS_WebAPIUser user = _userManager.FindByNameAsync(loginBuffer.UserName).GetAwaiter().GetResult();
            if (user == null)
            {
                rsp = new Response(RESULT.USER_NOT_EXIST, loginBuffer.DBName, Request.Headers["Host"], "User not found.");
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status401Unauthorized, rsp);
            }

            if (_userManager.CheckPasswordAsync(user, loginBuffer.Password).GetAwaiter().GetResult() == false)
            {
                rsp = new Response(RESULT.USER_PSWD_NOT_MATCH, loginBuffer.DBName, Request.Headers["Host"], "Wrong password.");
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status401Unauthorized, rsp);
            }

            string DBUID = "";
            try
            {
                DBUID = Rijndael.Decrypt(user.DBUID, Rijndael.EP_PASSPHRASE, Rijndael.EP_SALTVALUE, Rijndael.EP_HASHALGORITHM, Rijndael.EP_PASSWORDITERATIONS, Rijndael.EP_INITVECTOR, Rijndael.EP_KEYSIZE);
                if (DBUID.Length == 0)
                    throw new Exception("DBUID can't be empty.");
            }
            catch (Exception e)
            {
                rsp = new Response(RESULT.DECRYPT_DB_UID, loginBuffer.DBName, Request.Headers["Host"], $"Decrypt DBUID failed. {e.Message}");
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status401Unauthorized, rsp);
            }

            string DBPassword = "";
            try
            {
                DBPassword = Rijndael.Decrypt(user.DBPassword, Rijndael.EP_PASSPHRASE, Rijndael.EP_SALTVALUE, Rijndael.EP_HASHALGORITHM, Rijndael.EP_PASSWORDITERATIONS, Rijndael.EP_INITVECTOR, Rijndael.EP_KEYSIZE);
                if (DBPassword.Length == 0)
                    throw new Exception("DBPassword can't be empty.");
            }
            catch (Exception e)
            {
                rsp = new Response(RESULT.DECRYPT_DB_PASSWORD, loginBuffer.DBName, Request.Headers["Host"], $"Decrypt DBPassword failed. {e.Message}");
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status401Unauthorized, rsp);
            }

            if (user.DBName.Trim().ToUpper() != loginBuffer.DBName.Trim().ToUpper())
            {
                rsp = new Response(RESULT.DBNAME_NOT_MATCH, loginBuffer.DBName, Request.Headers["Host"], $"userDBName: {user.DBName}; bufferDBName: {loginBuffer.DBName}");
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status401Unauthorized, rsp);
            }

            var userRoles = _userManager.GetRolesAsync(user).GetAwaiter().GetResult();
            var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new Claim("DBName", user.DBName),
                    new Claim("DBUID", DBUID),
                    new Claim("DBPassword", DBPassword),
                };

            foreach (var userrole in userRoles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, userrole));
            }

            var authSigninKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWTSecret"]));
            var token = new JwtSecurityToken(issuer: _configuration["JWTValidIssuer"],
                                             audience: _configuration["JWTValidAudience"],
                                             expires: DateTime.Now.AddMinutes(Convert.ToInt32(_configuration["JWTExpireMinutes"])),
                                             claims: authClaims,
                                             signingCredentials: new SigningCredentials(authSigninKey, SecurityAlgorithms.HmacSha256));

            var strToken = (new JwtSecurityTokenHandler().WriteToken(token)).ToString();
            rsp = new Response(RESULT.SUCCESS, loginBuffer.DBName, Request.Headers["Host"], strToken);
            _logger.LogInformation(rsp.ToString());
            return StatusCode(StatusCodes.Status200OK, rsp);
        }
    }
}