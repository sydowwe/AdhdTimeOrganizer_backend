using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activity;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.service;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Command.application.service.activity;



public class RoleService(IRoleRepository repository, ILoggedUserService loggedUserService, IMapper autoMapper)
    : BaseCrudServiceWithUser<Role, NameTextColorIconRequest, NameTextColorIconResponse,IRoleRepository>(repository, loggedUserService, autoMapper), IRoleService
{

    //TODO Spravit ako serviceResult
    public async Task<NameTextColorIconResponse?> GetByNameAsync(string name)
    {
        return await _repository.GetAsQueryable().Where(r=>r.Name.Equals(name) && r.UserId == _loggedUserService.GetUserId).ProjectTo<NameTextColorIconResponse>(mapper.ConfigurationProvider).FirstOrDefaultAsync();
    }
    public async Task CreateDefaultItems(long newUserId)
    {
        await _repository.AddRangeAsync(
            [
                new Role{UserId = newUserId, Name = "Planner task", Text = "Quickly created activities in task planner", Color = "", Icon = "calendar-days"},
                new Role{UserId = newUserId, Name =  "To-do list task", Text = "Quickly created activities in to-do list", Color = "", Icon = "list-check"},
                new Role{UserId = newUserId, Name =  "Routine task", Text = "Quickly created activities in routine to-do list", Color = "", Icon = "recycle"}
            ]
        );
    }
};