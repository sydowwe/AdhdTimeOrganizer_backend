using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.@interface.activity;

public interface ICategoryService : IBaseWithUserService<Category, NameTextColorIconRequest, NameTextColorIconResponse>
{
}