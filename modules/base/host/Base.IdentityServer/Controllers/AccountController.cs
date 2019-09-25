﻿using IdentityModel.Client;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IdentityModel;
using IdentityServer4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Account.Web.Areas.Account.Controllers.Models;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.Configuration;
using Volo.Abp.Identity;
using Volo.Abp.Validation;
//using Volo.Abp.IdentityModel;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Application.Services;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Users;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using System.Linq;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.PermissionManagement;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Uow;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.Identity.EntityFrameworkCore;

namespace Base.Controllers
{

    public class RoleDto:IdentityRoleDto
    {

        public List<string> grantPermission = new List<string>();
    }


    public class RolePerssionReq:EntityDto<Guid>
    {

        public List<string> grantPermission
        {
            get; set;
        }

    }

    /// <summary>
    /// 用户权限及角色
    /// </summary>
    public class RoleAndPermissionByUser:RolePerssionReq
    {
        public string[] grantRoles
        {
            get;set;
        }
    }


    public class UserRole:IdentityUserRole
    {
        public UserRole(Guid UserId,Guid RoleId)
        {
            this.UserId = UserId;
            this.RoleId = RoleId;
        }
    }




    [RemoteService]
    [Route("/api/[controller]/[action]")]
    public class AccountController : AbpController
    {
        private readonly IdentityUserManager _userManager;
        private readonly IConfiguration _configuration;
        private readonly ICurrentTenant _currentTenant;
        private readonly AspNetCoreMultiTenancyOptions _aspNetCoreMultiTenancyOptions;
        //private readonly IIdentityModelAuthenticationService _authenticator;
        private readonly IProfileAppService _profileAppService;
        private readonly IIdentityRoleAppService _roleAppService;
        private readonly IIdentityUserAppService _userAppService;
        private readonly IAbpAuthorizationPolicyProvider _abpAuthorizationPolicyProvider;
        private readonly IAbpAuthorizationService _authorizationService;
        private readonly IPermissionDefinitionManager _permissionDefinitionManager;
        private readonly IRepository<PermissionGrant> _permissionGrant;
        private readonly IRepository<IdentityUser> _identityUser;
        private readonly IRepository<IdentityRole> _identityRole;


        public AccountController(IdentityUserManager userManager,
            IConfigurationAccessor configurationAccessor,
            ICurrentTenant currentTenant,
            IOptions<AspNetCoreMultiTenancyOptions> options,
            IProfileAppService profileAppService,
            IRepository<IdentityUser> identityUser,
            IIdentityRoleAppService roleAppService,
            IIdentityUserAppService userAppService,
            IAbpAuthorizationPolicyProvider abpAuthorizationPolicyProvider,
            IAbpAuthorizationService authorizationService,
            IRepository<PermissionGrant> permissionGrant,
            IPermissionDefinitionManager permissionDefinitionManager,
            IRepository<IdentityRole> identityRole
            )
        { 
            _userManager = userManager;
            _currentTenant = currentTenant;
            _aspNetCoreMultiTenancyOptions = options.Value;
            _configuration = configurationAccessor.Configuration;
            _profileAppService = profileAppService;
            _roleAppService = roleAppService;
            _userAppService = userAppService;
            _abpAuthorizationPolicyProvider = abpAuthorizationPolicyProvider;
            _authorizationService = authorizationService;
            _permissionDefinitionManager = permissionDefinitionManager;
            //_authenticator = authenticator;
            _permissionGrant = permissionGrant;
            _identityUser = identityUser;
            _identityRole = identityRole;

            
        }


        [HttpGet("permission/{Id:guid}")]
        public async Task<IEnumerable<string>> getUserGrantPermission([FromRoute]string Id)=>
            //获取当前user权限
             _permissionGrant.Where(res => res.ProviderName == "User"&&res.ProviderKey == Id).ToList().Select(res=>res.Name);


        [UnitOfWork]
        [HttpPost("userPermission")]
        public async Task updateUserIsRoleAndPermission([FromBody]RoleAndPermissionByUser req)
        {
            string uId = req.Id.ToString();
            //清除当前user权限
            await _permissionGrant.DeleteAsync(res => res.ProviderName == "User" && res.ProviderKey == uId);
            //添加当前用户权限
            await addUserPermission(req.grantPermission, uId);
            //添加当前角色
            await _userAppService.UpdateRolesAsync(req.Id, new IdentityUserUpdateRolesDto() { RoleNames = req.grantRoles});
        }


        private async Task addUserPermission(IEnumerable<string> req,string uId)
        {
            req.ToList().ForEach(async res =>
            {
                await _permissionGrant.InsertAsync(new PermissionGrant(Guid.NewGuid(), res, "User", uId));
            });
        }






















        /// <summary>
        /// 获取角色 --带权限
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<ListResultDto<RoleDto>> getRolesAndGrantPermission([FromQuery]PagedAndSortedResultRequestDto req)
        {
            //返回数据
            var resultDto = new ListResultDto<RoleDto>();
            //分页获取角色
            var roleData = JsonConvert.DeserializeObject<List<RoleDto>>(JsonConvert.SerializeObject(_identityRole
                .AsQueryable().Skip(req.SkipCount).Take(req.MaxResultCount).ToList()));
            //获取所有role权限
            var permissionRoleData = _permissionGrant.Where(res => res.ProviderName == "Role").ToList();
            foreach (var role in roleData)
            {
                foreach (var permission in permissionRoleData)
                {
                    if (role.Name == permission.ProviderKey)
                    {
                        role.grantPermission?.Add(permission.Name);
                    }
                }
            }
            return new ListResultDto<RoleDto>()
            {
                Items = roleData
            };
        }

        [UnitOfWork]
        [HttpPost]
        public async Task UpdateRoleGrantPermission([FromBody]RolePerssionReq req)
        {
            //get当前角色
            IdentityRole role = await _identityRole.FirstOrDefaultAsync(res=>res.Id == req.Id);
            //清楚当前用户所有权限
            await _permissionGrant.DeleteAsync(r => r.ProviderKey == role.Name);
            //加入权限
            req.grantPermission.ForEach(async res =>
            {
                await _permissionGrant.InsertAsync(new PermissionGrant(Guid.NewGuid(),res, "Role",role.Name));
            });
        }

        /// <summary>
        /// DiscoveryClient方法提示在下一个版本被弃用，scope传递offline_access，可得到refresh_token值
        /// </summary>
        /// <param name="login"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> Token(UserLoginInfo login)
        {
            var dico = await DiscoveryClient.GetAsync(_configuration["AuthServer:Authority"]);
            if (dico.IsError)
            {
                Console.WriteLine(dico.Error);
                return Json(new { code = 0, data = dico.Error });
            }

            await ReplaceEmailToUsernameOfInputIfNeeds(login);

            var tokenClient = new TokenClient(dico.TokenEndpoint, _configuration["AuthServer:ClientId"], _configuration["AuthServer:ClientSecret"]);
            TokenResponse tokenresp = await tokenClient.RequestResourceOwnerPasswordAsync(
                login.UserNameOrEmailAddress,
                login.Password,
                "Base",
                extra: new Dictionary<string, string>
                {
                    {_aspNetCoreMultiTenancyOptions.TenantKey,login.TenanId?.ToString()}
                }
                );
            if (tokenresp.IsError)
            {
                Console.WriteLine(tokenresp.Error);
                return Json(new
                {
                    code = 0,
                    data = tokenresp.ErrorDescription,
                    message = tokenresp.Error
                });
            }

            return Json(new { code = 10000, data = tokenresp.Json });
        }

        protected virtual async Task ReplaceEmailToUsernameOfInputIfNeeds(UserLoginInfo login)
        {
            if (!ValidationHandler.IsValidEmailAddress(login.UserNameOrEmailAddress))
            {
                return;
            }

            var userByUsername = await _userManager.FindByNameAsync(login.UserNameOrEmailAddress);
            if (userByUsername != null)
            {
                return;
            }

            var userByEmail = await _userManager.FindByEmailAsync(login.UserNameOrEmailAddress);
            if (userByEmail == null)
            {
                return;
            }

            if (userByEmail.EmailConfirmed == false)
            {
                throw new UserFriendlyException("邮件未激活确认,无法使用邮件进行登录!");
            }

            login.UserNameOrEmailAddress = userByEmail.UserName;
        }

        /// <summary>
        /// 官方支持获取AccessToken的写法，传递offline_access也得不到refresh_token,除了改类库源码GetAccessTokenAsync，直接返回tokenResponse
        /// </summary>
        /// <param name="login"></param>
        /// <returns></returns>
        //public async Task<IActionResult> GetAccessToken(UserLoginInfo login)
        //{
        //    await ReplaceEmailToUsernameOfInputIfNeeds(login);

        //    var config = new IdentityClientConfiguration
        //    {
        //        Authority = _configuration["AuthServer:Authority"],
        //        ClientId = _configuration["AuthServer:ClientId"],
        //        ClientSecret = _configuration["AuthServer:ClientSecret"],
        //        GrantType = OidcConstants.GrantTypes.Password,
        //        UserName = login.UserNameOrEmailAddress,
        //        UserPassword = login.Password,
        //        Scope = "Pay"
        //    };

        //    string token = await _authenticator.GetAccessTokenAsync(config);

        //    return Json(new { code = 1, data = token });
        //}
        [HttpPost]
        public async Task<IActionResult> Info()
        {
            var profile = await _profileAppService.GetAsync();
            //var user = await _userAppService.GetAsync(CurrentUser.GetId());
            var roles = await _userAppService.GetRolesAsync(CurrentUser.GetId());

            var auth = await GetAuthConfigAsync();

            var data = new Dictionary<string, object>(3);
            data.Add("profile", profile);
            data.Add("roles", roles.Items);
            data.Add("auth", auth);

            return Json(new { code = 1, data = data });
        }
        private async Task<ApplicationAuthConfigurationDto> GetAuthConfigAsync()
        {
            var authConfig = new ApplicationAuthConfigurationDto();
            foreach (var policyName in await _abpAuthorizationPolicyProvider.GetPoliciesNamesAsync())
            {
                authConfig.Policies[policyName] = true;

                if (await _authorizationService.IsGrantedAsync(policyName))
                {
                    authConfig.GrantedPolicies[policyName] = true;
                }
            }

            foreach (var group in _permissionDefinitionManager.GetGroups())
            {
                authConfig.Policies[group.Name] = true;


                authConfig.GrantedPolicies[group.Name] = true;
            }

            return authConfig;
        }
        [HttpPost]
        public async Task<IActionResult> LogOut()
        {
            return Json(new { code = 1, data = "{msg:'success'}" });
        }
    }
}
