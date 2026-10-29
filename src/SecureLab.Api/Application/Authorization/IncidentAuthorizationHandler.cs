using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Authorization;

// ЛР 04: resource-based рішення для одного інциденту. Вхід: principal із cookie, операція і факти з БД.
// Дозвіл видається лише явною гілкою; усе, чого немає в матриці, лишається відмовою.
public sealed class IncidentAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, IncidentAccessFacts>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        IncidentAccessFacts incident)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var isOwner = incident.OwnerUserId == user.GetUserId();
        var isAnalyst = user.IsInRole(DbSeeder.AnalystRole);

        var allowed = requirement.Name switch
        {
            nameof(IncidentOperations.Read) => isOwner || isAnalyst,
            nameof(IncidentOperations.Update) => isOwner,
            // Власник прибирає лише запис, який ще ніхто не почав аналізувати.
            nameof(IncidentOperations.Delete) => isOwner && incident.Status == IncidentStatus.New,
            // Роль перевіряється тут; допустимість переходу New -> Triaged перевіряє endpoint після рішення.
            nameof(IncidentOperations.Triage) => isAnalyst,
            nameof(IncidentOperations.ReadInternalComments) => isAnalyst,
            _ => false
        };

        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
