using AMS_WebAPI.Areas.Identity.Data;
using AMS_WebAPI.Data;
using AMS_WebAPI.Models;
using API.Common.AMS;
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
using System.Linq;
using System.Net;
using System.Net.Http;
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

        [AllowAnonymous]
        [HttpPost("testPost1")]
        public async Task<IActionResult> TestPost1()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string ss = await reader.ReadToEndAsync();
                return Ok(new Response() { Status = RESULT.SUCCESS, Msg = "Invoke Test_Anonymous successfully" });
            }
        }

        [HttpPost("PostTestNoBody_Anonymous")]
        [AllowAnonymous]
        public IActionResult PostTestNoBody_Anonymous()
        {
            var dd = 0;

            

            return Ok(new Response { Status = RESULT.SUCCESS, Msg = "Invoke Test_Anonymous successfully" });
        }

        [HttpPost("PostTestWithBody_Anonymous")]
        [AllowAnonymous]
        public IActionResult PostTestWithBody_Anonymous([FromBody] string value)
        {
            var dd = 0;
            
            return Ok(new Response { Status = RESULT.SUCCESS, Msg = "Invoke Test_Anonymous successfully" });
        }

        [HttpGet("Test_Anonymous")]
        [AllowAnonymous]
        public IActionResult Test_Anonymous([FromHeader] string TestBlock)
        {
            var dd = 0;
            return Ok(new Response { Status = RESULT.SUCCESS, Msg = "Invoke Test_Anonymous successfully" });
        }

        [HttpGet("Test_Authrized")]
        public IActionResult Test_Authrized()
        {
            var dd = 0;
            return Ok(new Response { Status = RESULT.SUCCESS, Msg = "Invoke Test_Authrized successfully" });
        }

        [HttpPost("Post1")]
        public HttpResponseMessage Post1([FromBody] string name)
        {
            var dd = 0;
            return new HttpResponseMessage(HttpStatusCode.OK);
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
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password))
            {
                var userRoles = await _userManager.GetRolesAsync(user);
                var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                };

                foreach (var userrole in userRoles)
                {
                    authClaims.Add(new Claim(ClaimTypes.Role, userrole));
                }

                var authSigninKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Secret"]));
                var token = new JwtSecurityToken(issuer: _configuration["JWT:ValidIssuer"],
                                                 audience: _configuration["JWT:ValidAudience"],
                                                 expires: DateTime.Now.AddHours(5),
                                                 claims: authClaims,
                                                 signingCredentials: new SigningCredentials(authSigninKey, SecurityAlgorithms.HmacSha256));

                return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
            }

            return Unauthorized();
        }

        [HttpPost]
        [Route("TestPost")]
        public IActionResult TestPost()
        {
            _logger.LogInformation($"Entered TestPost {DateTime.Now.ToShortTimeString()}");
            return Ok(new Response { Status = RESULT.SUCCESS, Msg = "TestPost successfully" });
        }
    }
}
