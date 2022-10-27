#  Extract template

from src/armtemplates

dotnet run extract --sourceApimName <DEV-APIM-NAME> --destinationApimName <DESTINATION-APIM-NAME> --resourceGroup <RESOURCE-GROUP-NAME> --fileFolder c:\\temp\\apim-extract.
dotnet run extract --sourceApimName apim-unai --destinationApimName ApimAz204Unai --resourceGroup att-az204-apim-rg --fileFolder Extracted-Templates

# Open Id authentication for GitHub Actions
https://learn.microsoft.com/en-us/azure/azure-resource-manager/templates/deploy-github-actions?tabs=openid
