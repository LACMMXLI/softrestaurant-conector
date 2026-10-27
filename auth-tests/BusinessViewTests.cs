using RestaurantAgent.CentralApi;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class BusinessViewTests
{
    [Fact]
    public void Members_json_from_the_admin_query_maps_to_summaries()
    {
        var owner = Guid.NewGuid();
        var json = $$"""[{"userId":"{{owner}}","email":"dueno@x.com","displayName":"Dueño Uno","role":"OWNER"},{"userId":"{{Guid.NewGuid()}}","email":"g@x.com","displayName":"Gerente","role":"MANAGER"}]""";

        var members = BusinessRegistry.ParseMembers(json);

        Assert.Equal(2, members.Count);
        Assert.Equal(owner, members[0].UserId);
        Assert.Equal("OWNER", members[0].Role);
        Assert.Equal("Dueño Uno", members[0].DisplayName);
    }

    [Fact]
    public void A_business_without_members_yields_an_empty_list()
    {
        Assert.Empty(BusinessRegistry.ParseMembers("[]"));
    }
}
