using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Subscriptions.Api;
public sealed class ContractFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation,OperationFilterContext context)
    {
        foreach(var status in new[]{400,401,403,404,409,413,415,429,500,503})
            operation.Responses!.TryAdd(status.ToString(),new OpenApiResponse { Description="Error: "+status,Content=new Dictionary<string,OpenApiMediaType>{["application/json"]=new(){Schema=context.SchemaGenerator.GenerateSchema(typeof(ErrorResponse),context.SchemaRepository)}} });
        if(context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any()
            && !context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            operation.Security=[new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)]=[] }];
        if(context.ApiDescription.RelativePath=="api/v1/subscriptions/{subscriptionId}/payments")
        {
            operation.Parameters ??=[];operation.Parameters.Add(new OpenApiParameter{Name="Idempotency-Key",In=ParameterLocation.Header,Required=true,Schema=new OpenApiSchema{Type=JsonSchemaType.String,MinLength=1,MaxLength=128},Description="Owner scoped key; replay returns original 201/body."});
        }
        foreach(var response in operation.Responses!.Values)
        {
            if(response is OpenApiResponse item) { item.Headers ??=new Dictionary<string,IOpenApiHeader>();item.Headers["X-Correlation-ID"]=new OpenApiHeader{Schema=new OpenApiSchema{Type=JsonSchemaType.String}}; }
        }
    }
}
