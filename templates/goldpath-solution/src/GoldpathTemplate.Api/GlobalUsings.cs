global using Goldpath;
global using Mediant.Abstractions;
global using Mediant.AspNetCore.Attributes;
global using Mediant.Results;
//#if (UseCleanArch)
global using GoldpathTemplate.Domain.Orders;
global using GoldpathTemplate.Application.Orders;
global using GoldpathTemplate.Application.Orders.Features;
global using GoldpathTemplate.Infrastructure.Persistence;
//#if (UseBulk)
global using GoldpathTemplate.Application.Orders.Import;
//#endif
//#if (UseNotification)
global using GoldpathTemplate.Application.Orders.Notifications;
//#endif
//#if (UseCampaign)
global using GoldpathTemplate.Application.Orders.Campaigns;
//#endif
//#else
global using GoldpathTemplate.Api.Orders;
//#if (UseBulk)
global using GoldpathTemplate.Api.Orders.Import;
//#endif
//#if (UseNotification)
global using GoldpathTemplate.Api.Orders.Notifications;
//#endif
//#if (UseCampaign)
global using GoldpathTemplate.Api.Orders.Campaigns;
//#endif
//#endif
