using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.@base.core;

/// <summary>
/// Closes Framework's <see cref="BaseLookupWithUser{TUser}"/> over this portal's <see cref="User"/>.
/// The behaviour lives in Framework — keep this a plain closing type.
/// </summary>
public abstract class BaseLookupWithUser : BaseLookupWithUser<User>;
