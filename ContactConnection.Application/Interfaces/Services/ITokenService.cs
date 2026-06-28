using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

public interface ITokenService
{
    string GenerateToken(Agent agent, Tenant tenant, Role? role = null);
    string GeneratePreAuthToken(Agent agent, Tenant tenant);
}
