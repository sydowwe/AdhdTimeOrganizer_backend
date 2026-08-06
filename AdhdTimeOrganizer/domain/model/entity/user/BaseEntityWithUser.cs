using Sydowwe.Framework.domain.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.user;

/// <summary>
/// Closes Framework's <see cref="BaseEntityWithUser{TUser}"/> over this portal's <see cref="User"/>.
/// C# can't infer <c>TUser</c> from a constraint, so this shim keeps every entity declaration and
/// every <c>IsManyWithOneUser</c> call site from having to spell the type argument out. The behaviour
/// — including the <c>UserId</c> stamping contract — lives in Framework; keep this a plain closing type.
/// </summary>
public abstract class BaseEntityWithUser : BaseEntityWithUser<User>;