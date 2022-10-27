#  Extract template

from src/armtemplates

dotnet run extract --sourceApimName <DEV-APIM-NAME> --destinationApimName <DESTINATION-APIM-NAME> --resourceGroup <RESOURCE-GROUP-NAME> --fileFolder c:\\temp\\apim-extract.
dotnet run extract --sourceApimName apim-unai --destinationApimName ApimAz204Unai --resourceGroup att-az204-apim-rg --fileFolder Extracted-Templates

# Github Action
Based on: 
https://github.com/unaihuete-org/APIM-DevOps-Sample/blob/main/docs/AzDO-Example.md
