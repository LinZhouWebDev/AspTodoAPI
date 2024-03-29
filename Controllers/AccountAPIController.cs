﻿using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AspTodo.Models;
using AspTodo.Models.Dtos;
using AspTodo.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AspTodo.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]/[action]")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class AccountAPI : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IEmailSender _emailSender;

        public AccountAPI(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration configuration,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _roleManager = roleManager;
            _emailSender = emailSender;
        }

        // test data action for authorzation 
        [HttpGet]
        public IEnumerable<string> Users()
        {
            return _userManager.Users.Select(u => u.UserName);
        }


        [HttpPost]
        [AllowAnonymous]
        public async Task<object> Register([FromBody] RegisterDto model)
        {
            // quick hack
            await CreateInitialRolesAsync();

            if (ModelState.IsValid)
            {
                // check if user exists first
                var check = _userManager.FindByEmailAsync(model.Email);
                if (check.Result != null)
                {
                    return StatusCode(409, new { Error = "Email Already Exists" });
                }
                var user = new ApplicationUser { UserName = model.Email, Email = model.Email };
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    // default to free role
                    string roleToAdd = String.IsNullOrEmpty(model.Role) ? "FREE" : (await _roleManager.RoleExistsAsync(model.Role) ? model.Role : "FREE");
                    bool userRoleSucceeded = await AddUserRole(model.Email, roleToAdd);
                    if (!userRoleSucceeded)
                    {
                        return StatusCode(500, new { Message = "Role Assignment Failed." });
                    }

                    // send confirmation email 
                    await SendConfirmationEmail(user);

                    // change this part to not auto login after register
                    await _signInManager.SignInAsync(user, false);
                    //ApplicationUser appUser = _userManager.Users.SingleOrDefault(r => r.Email == model.Email);
                    ApplicationUser appUser = await _userManager.FindByEmailAsync(model.Email);
                    return await AuthOkWithToken(appUser);
                }
                else
                {
                    // general errors
                    return StatusCode(500, new { result.Errors });
                }
            }
            return BadRequest(ModelState);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<object> Login([FromBody] LoginDto model)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, false, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    //ApplicationUser appUser = _userManager.Users.SingleOrDefault(r => r.Email == model.Email);
                    ApplicationUser appUser = await _userManager.FindByEmailAsync(model.Email);
                    return await AuthOkWithToken(appUser);
                }

                if (result.IsLockedOut)
                {
                    return StatusCode(403, new { Message = "User is locked out due to too many failed attempts." }); ;
                }

                return StatusCode(401, new { Message = "Incorrect username or password." });
            }
            return BadRequest(ModelState);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto model)
        {
            if (ModelState.IsValid)
            {
                ApplicationUser user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    // return Ok() if don't want to reveal that the user does not exist or is not confirmed
                    return NotFound();
                }
                var result = await _userManager.ConfirmEmailAsync(user, model.Code);
                if (result.Succeeded)
                {
                    return Ok();
                }
                return StatusCode(403, new { result.Errors });
            }
            return BadRequest(ModelState);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<object> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            if (ModelState.IsValid)
            {
                ApplicationUser user = await _userManager.FindByEmailAsync(model.Email);
                //if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
                if (user == null)
                {
                    // return Ok() if don't want to reveal that the user does not exist or is not confirmed
                    return NotFound(new { message = "User not found." });
                }
                string code = await _userManager.GeneratePasswordResetTokenAsync(user);
                await _emailSender.SendEmailAsync(model.Email, "Reset Password",
                    $"Please reset your password by using this code: {code}");
                return Ok();
            }
            return BadRequest(ModelState);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<object> ResetPassword([FromBody] ResetPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                // return Ok() if don't want to reveal that the user does not exist or is not confirmed
                return NotFound(new { message = "User not found." });
            }
            var result = await _userManager.ResetPasswordAsync(user, model.Code, model.Password);
            if (result.Succeeded)
            {
                return Ok();
            }
            else
            {
                return StatusCode(500, new { result.Errors });
            }

        }

        [HttpGet]
        public async Task<object> UserInfo()
        {
            string userID = HttpContext.User.Claims.ElementAt(2).Value;
            ApplicationUser user = await _userManager.FindByIdAsync(userID);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }
            UserInfoDto userInfo = await GetUserInfo(user);
            return Ok(userInfo);
        }

        [HttpPost]
        public async Task<object> UpdateProfile([FromBody] UserInfoDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            string userID = HttpContext.User.Claims.ElementAt(2).Value;
            ApplicationUser user = await _userManager.FindByIdAsync(userID);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (model.Email != user.Email)
            {
                var setEmailResult = await _userManager.SetEmailAsync(user, model.Email);
                if (!setEmailResult.Succeeded)
                {
                    return StatusCode(500, new { setEmailResult.Errors });
                }
                var setUsernameResult = await _userManager.SetUserNameAsync(user, model.Email);
                if (!setUsernameResult.Succeeded)
                {
                    return StatusCode(500, new { setUsernameResult.Errors });
                }
            }
            return Ok();
        }

        [HttpPost]
        public async Task<object> ChangePassword([FromBody] ChangePasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string userID = HttpContext.User.Claims.ElementAt(2).Value;
            ApplicationUser user = await _userManager.FindByIdAsync(userID);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var changePasswordResult = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (!changePasswordResult.Succeeded)
            {
                return StatusCode(403, new { changePasswordResult.Errors });
            }

            return Ok();
        }

        #region Helpers
        private async Task<UserInfoDto> GetUserInfo(ApplicationUser user)
        {

            string role = await _userManager.IsInRoleAsync(user, "PRO") ? "PRO" : "FREE";

            UserInfoDto model = new UserInfoDto
            {
                Role = role,
                Email = user.Email
            };
            return model;
        }

        private async Task<bool> SendConfirmationEmail(ApplicationUser user)
        {
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _emailSender.SendEmailAsync(user.Email, "Confirm your email",
                $"Please confirm your account with this code: {code}");
            return true;
        }

        private async Task<object> AuthOkWithToken(ApplicationUser appUser)
        {
            UserInfoDto userInfo = await GetUserInfo(appUser);
            string token = await GenerateJwtToken(appUser);
            return Ok(new { token, userInfo });
        }

        private async Task<string> GenerateJwtToken(IdentityUser user)
        {
            var claims = new List<Claim> {
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id)
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                            _configuration["TokenInformation:Key"]));
            var expires = DateTime.Now.AddDays(Convert.ToDouble(1));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["TokenInformation:Issuer"],
                audience: _configuration["TokenInformation:Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );
            string formattedToken = new JwtSecurityTokenHandler().WriteToken(token);
            return formattedToken;
        }


        public async Task<bool> CreateInitialRolesAsync()
        {
            string[] roleNames = { "PRO", "FREE" };
            IdentityResult roleResult;

            foreach (var roleName in roleNames)
            {
                var roleExist = await _roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    roleResult = await _roleManager.CreateAsync(new IdentityRole(roleName));
                    if (!roleResult.Succeeded)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public async Task<bool> AddUserRole(string email, string roleName)
        {
            var user = await _userManager.FindByEmailAsync(email);

            IdentityRole applicationRole = await _roleManager.FindByNameAsync(roleName);

            if (applicationRole != null)
            {
                IdentityResult roleResult = await _userManager.AddToRoleAsync(user, roleName);
            }

            return true;
        }

        #endregion
    }
}