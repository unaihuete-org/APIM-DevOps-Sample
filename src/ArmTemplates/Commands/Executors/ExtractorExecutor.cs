﻿using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Constants;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Exceptions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Extensions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.FileHandlers;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Templates.Abstractions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Templates.Logger;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Extractor.EntityExtractors;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Extractor.EntityExtractors.Abstractions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Extractor.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.Management.ApiManagement.ArmTemplates.Commands.Executors
{
    public class ExtractorExecutor
    {
        readonly ExtractorParameters extractorParameters;

        readonly IApiExtractor apiExtractor;
        readonly IApiVersionSetExtractor apiVersionSetExtractor;
        readonly IAuthorizationServerExtractor authorizationServerExtractor;
        readonly IBackendExtractor backendExtractor;
        readonly ILoggerExtractor loggerExtractor;
        readonly IMasterTemplateExtractor masterTemplateExtractor;
        readonly IPolicyExtractor policyExtractor;
        readonly IProductApiExtractor productApiExtractor;
        readonly IProductExtractor productExtractor;
        readonly IPropertyExtractor propertyExtractor;
        readonly IApiTagExtractor apiTagExtractor;
        readonly ITagExtractor tagExtractor;

        public ExtractorExecutor(ExtractorConsoleAppConfiguration extractorConfig)
            : this(new ExtractorParameters(extractorConfig))
        {
            EntityExtractorBase.BaseUrl = string.IsNullOrEmpty(extractorConfig.ServiceBaseUrl)
                ? EntityExtractorBase.BaseUrl
                : extractorConfig.ServiceBaseUrl;
        }

        public ExtractorExecutor(
            ExtractorParameters extractorParameters,
            IApiExtractor apiExtractor = null,
            IApiVersionSetExtractor apiVersionSetExtractor = null,
            IAuthorizationServerExtractor authorizationServerExtractor = null,
            IBackendExtractor backendExtractor = null,
            ILoggerExtractor loggerExtractor = null,
            IMasterTemplateExtractor masterTemplateExtractor = null,
            IPolicyExtractor policyExtractor = null,
            IProductApiExtractor productApiExtractor = null,
            IProductExtractor productExtractor = null,
            IPropertyExtractor propertyExtractor = null,
            IApiTagExtractor apiTagExtractor = null,
            ITagExtractor tagExtractor = null)
        {
            this.extractorParameters = extractorParameters;

            this.apiExtractor = apiExtractor ?? new ApiExtractor();
            this.apiVersionSetExtractor = apiVersionSetExtractor ?? new ApiVersionSetExtractor();
            this.authorizationServerExtractor = authorizationServerExtractor ?? new AuthorizationServerExtractor();
            this.backendExtractor = backendExtractor ?? new BackendExtractor();
            this.loggerExtractor = loggerExtractor ?? new LoggerExtractor();
            this.masterTemplateExtractor = masterTemplateExtractor ?? new MasterTemplateExtractor();
            this.policyExtractor = policyExtractor ?? new PolicyExtractor();
            this.productApiExtractor = productApiExtractor ?? new ProductApiExtractor();
            this.propertyExtractor = propertyExtractor ?? new PropertyExtractor();
            this.productExtractor = productExtractor ?? new ProductExtractor();
            this.apiTagExtractor = apiTagExtractor ?? new ApiTagExtractor();
            this.tagExtractor = tagExtractor ?? new TagExtractor();
        }

        /// <summary>
        /// Retrieves parameters for extractor from the configuration and runs generation automatically.
        /// For specific template generation scenarios, please, use other exposed methods
        /// </summary>
        public async Task ExecuteGenerationBasedOnConfiguration()
        {
            Logger.LogInformation("API Management Template");
            Logger.LogInformation("Connecting to {0} API Management Service on {1} Resource Group ...", this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup);

            if (this.extractorParameters.SplitApis)
            {
                Logger.LogInformation("Starting templates with splitting for each API extraction...");
                await this.GenerateSplitAPITemplates();
                await this.GenerateTemplates(this.extractorParameters.FilesGenerationRootDirectory);
            }
            else if (!string.IsNullOrEmpty(this.extractorParameters.ApiVersionSetName))
            {
                Logger.LogInformation("Starting API version set templates extraction...");
                await this.GenerateAPIVersionSetTemplates();
            }
            else if (!this.extractorParameters.MultipleApiNames.IsNullOrEmpty())
            {
                Logger.LogInformation("Launching multiple APIs templates extraction...");
                await this.GenerateMultipleAPIsTemplates();
            }
            else if (!string.IsNullOrEmpty(this.extractorParameters.SingleApiName) && this.extractorParameters.IncludeAllRevisions)
            {
                Logger.LogInformation("Launching single API with revisions templates extraction...");
                await this.GenerateSingleAPIWithRevisionsTemplates();
            }
            else
            {
                Logger.LogInformation("No specific parameters are set for template generation...");
                await this.GenerateTemplates(this.extractorParameters.FilesGenerationRootDirectory, singleApiName: this.extractorParameters.SingleApiName);
            }
        }

        /// <summary>
        /// Generates policy templates in the desired folder
        /// </summary>
        /// <param name="baseFilesGenerationDirectory">name of base folder where to save files</param>
        /// <returns>generated global service policy template</returns>
        public async Task<Template> GeneratePolicyTemplateAsync(string baseFilesGenerationDirectory)
        {
            var globalServicePolicyTemplate = await this.policyExtractor.GenerateGlobalServicePolicyTemplateAsync(
                this.extractorParameters,
                baseFilesGenerationDirectory);

            if (globalServicePolicyTemplate.HasResources)
            {
                await FileWriter.SaveAsJsonAsync(
                    globalServicePolicyTemplate,
                    directory: baseFilesGenerationDirectory,
                    fileName: this.extractorParameters.FileNames.GlobalServicePolicy);
            }

            return globalServicePolicyTemplate;
        }

        /// <summary>
        /// Generates split api templates / folders for each api in this sourceApim 
        /// </summary>
        /// <returns></returns>
        async Task GenerateSplitAPITemplates()
        {
            // Generate folders based on all apiversionset
            var apiDictionary = await this.GetAllAPIsDictionary(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup);

            // Generate templates based on each API/APIversionSet
            foreach (var versionSetEntry in apiDictionary)
            {
                string apiFileFolder = this.extractorParameters.FilesGenerationRootDirectory;

                // if it's APIVersionSet, generate the versionsetfolder for templates
                if (versionSetEntry.Value.Count > 1)
                {
                    // this API has VersionSet
                    string apiDisplayName = versionSetEntry.Key;

                    // create apiVersionSet folder
                    apiFileFolder = string.Concat(@apiFileFolder, $@"/{apiDisplayName}");
                    Directory.CreateDirectory(apiFileFolder);

                    // create master templates for each apiVersionSet
                    string versionSetFolder = string.Concat(@apiFileFolder, this.extractorParameters.FileNames.VersionSetMasterFolder);
                    Directory.CreateDirectory(versionSetFolder);
                    await this.GenerateTemplates(versionSetFolder, multipleApiNames: versionSetEntry.Value);

                    Logger.LogInformation($@"Finish extracting APIVersionSet {versionSetEntry.Key}");
                }

                // Generate templates for each api 
                foreach (string apiName in versionSetEntry.Value)
                {
                    // create folder for each API
                    string tempFileFolder = string.Concat(@apiFileFolder, $@"/{apiName}");
                    Directory.CreateDirectory(tempFileFolder);
                    // generate templates for each API
                    await this.GenerateTemplates(tempFileFolder, singleApiName: apiName);

                    Logger.LogInformation($@"Finish extracting API {apiName}");
                }
            }
        }

        /// <summary>
        /// Generates master template for each API within this version set and an extra master template to link these apis
        /// </summary>
        async Task GenerateAPIVersionSetTemplates()
        {
            // get api dictionary and check api version set
            var apiDictionary = await this.GetAllAPIsDictionary(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup);
            if (!apiDictionary.ContainsKey(this.extractorParameters.ApiVersionSetName))
            {
                throw new NoApiVersionSetWithSuchNameFoundException("API Version Set with this name doesn't exist");
            }
            else
            {
                Logger.LogInformation("Started extracting the API version set {0}", this.extractorParameters.ApiVersionSetName);

                foreach (string apiName in apiDictionary[this.extractorParameters.ApiVersionSetName])
                {
                    // generate seperate folder for each API
                    string apiFileFolder = string.Concat(this.extractorParameters.FilesGenerationRootDirectory, $@"/{apiName}");
                    Directory.CreateDirectory(apiFileFolder);
                    await this.GenerateTemplates(apiFileFolder, singleApiName: apiName);
                }

                // create master templates for this apiVersionSet 
                string versionSetFolder = string.Concat(this.extractorParameters.FilesGenerationRootDirectory, this.extractorParameters.FileNames.VersionSetMasterFolder);
                Directory.CreateDirectory(versionSetFolder);
                await this.GenerateTemplates(versionSetFolder, multipleApiNames: apiDictionary[this.extractorParameters.ApiVersionSetName]);

                Logger.LogInformation($@"Finished extracting APIVersionSet {this.extractorParameters.ApiVersionSetName}");
            }
        }

        /// <summary>
        /// Generates templates for multiple specified APIs
        /// </summary>
        async Task GenerateMultipleAPIsTemplates()
        {
            if (this.extractorParameters.MultipleApiNames.IsNullOrEmpty())
            {
                throw new Exception("MultipleAPIs parameter doesn't have any data");
            }

            Logger.LogInformation("Started extracting multiple APIs (amount is {0} APIs)", this.extractorParameters.MultipleApiNames.Count);

            foreach (string apiName in this.extractorParameters.MultipleApiNames)
            {
                // generate seperate folder for each API
                string apiFileFolder = string.Concat(this.extractorParameters.FilesGenerationRootDirectory, $@"/{apiName}");
                Directory.CreateDirectory(apiFileFolder);
                await this.GenerateTemplates(apiFileFolder, singleApiName: apiName);
            }

            // create master templates for these apis 
            string groupApiFolder = string.Concat(this.extractorParameters.FilesGenerationRootDirectory, this.extractorParameters.FileNames.GroupAPIsMasterFolder);
            Directory.CreateDirectory(groupApiFolder);
            await this.GenerateTemplates(groupApiFolder, multipleApiNames: this.extractorParameters.MultipleApiNames);

            Logger.LogInformation($@"Finished extracting mutiple APIs");
        }

        async Task GenerateSingleAPIWithRevisionsTemplates()
        {
            Logger.LogInformation("Extracting singleAPI {0} with revisions", this.extractorParameters.SingleApiName);

            // Get all revisions for this api
            string revisions = await this.apiExtractor.GetAPIRevisionsAsync(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup, this.extractorParameters.SingleApiName);
            JObject revs = JObject.Parse(revisions);
            string currentRevision = null;
            List<string> revList = new List<string>();

            // Generate seperate folder for each API revision
            for (int i = 0; i < ((JContainer)revs["value"]).Count; i++)
            {
                string apiID = ((JValue)revs["value"][i]["apiId"]).Value.ToString();
                string singleApiName = apiID.Split("/")[2];
                if (((JValue)revs["value"][i]["isCurrent"]).Value.ToString().Equals("True"))
                {
                    currentRevision = singleApiName;
                }

                string revFileFolder = string.Concat(this.extractorParameters.FilesGenerationRootDirectory, $@"/{singleApiName}");
                Directory.CreateDirectory(revFileFolder);
                await this.GenerateTemplates(revFileFolder, singleApiName: singleApiName);
                revList.Add(singleApiName);
            }

            if (currentRevision == null)
            {
                throw new Exception($"Revision {this.extractorParameters.SingleApiName} doesn't exist, something went wrong!");
            }
            
            // generate revisions master folder
            string revisionMasterFolder = string.Concat(this.extractorParameters.FilesGenerationRootDirectory, this.extractorParameters.FileNames.RevisionMasterFolder);
            Directory.CreateDirectory(revisionMasterFolder);

            Template apiRevisionTemplate = await this.apiExtractor.GenerateAPIRevisionTemplateAsync(
                this.extractorParameters,
                currentRevision,
                revList,
                revisionMasterFolder);

            await this.GenerateTemplates(revisionMasterFolder, apiTemplate: apiRevisionTemplate);
        }

        /// <summary>
        /// three condistions to use this function:
        /// 1. singleApiName is null, then generate one master template for the multipleAPIs in multipleApiNams
        /// 2. multipleApiNams is null, then generate separate folder and master template for each API 
        /// 3. when both singleApiName and multipleApiNams is null, then generate one master template to link all apis in the sourceapim
        /// </summary>
        async Task GenerateTemplates(
            string baseFilesGenerationDirectory,
            string singleApiName = null,
            List<string> multipleApiNames = null,
            Template apiTemplate = null)
        {
            if (!string.IsNullOrEmpty(singleApiName) && !multipleApiNames.IsNullOrEmpty())
            {
                throw new SingleAndMultipleApisCanNotExistTogetherException("Can't specify single API and multiple APIs to extract at the same time");
            }

            // Get all Apis that will be extracted
            List<string> apisToExtract = new List<string>();
            if (!string.IsNullOrEmpty(singleApiName))
            {
                apisToExtract.Add(singleApiName);
            }
            else if (!multipleApiNames.IsNullOrEmpty())
            {
                apisToExtract.AddRange(multipleApiNames);
            }
            else
            {
                Logger.LogInformation($"There were no `{nameof(singleApiName)}` or `{nameof(multipleApiNames)}` specified. Loading all API names from {nameof(ExtractorParameters.SourceApimName)} directly...");
                List<string> allApis = await this.apiExtractor.GetAllApiNamesAsync(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup);
                apisToExtract.AddRange(allApis);
            }

            Dictionary<string, object> apiLoggerId = null;
            if (this.extractorParameters.ParameterizeApiLoggerId)
            {
                apiLoggerId = await this.GetAllReferencedLoggers(apisToExtract, this.extractorParameters);
            }

            if (apiTemplate == null)
            {
                apiTemplate = await this.apiExtractor.GenerateAPIsARMTemplateAsync(this.extractorParameters, singleApiName, multipleApiNames, baseFilesGenerationDirectory);
            }

            // generate different templates using extractors and write to output

            var globalServicePolicyTemplate = await this.GeneratePolicyTemplateAsync(baseFilesGenerationDirectory);

            List<TemplateResource> apiTemplateResources = apiTemplate.Resources.ToList();
            Template apiVersionSetTemplate = await this.apiVersionSetExtractor.GenerateAPIVersionSetsARMTemplateAsync(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup, singleApiName, apiTemplateResources);
            Template authorizationServerTemplate = await this.authorizationServerExtractor.GenerateAuthorizationServersARMTemplateAsync(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup, singleApiName, apiTemplateResources);
            Template loggerTemplate = await this.loggerExtractor.GenerateLoggerTemplateAsync(this.extractorParameters, singleApiName, apiTemplateResources, apiLoggerId);
            Template productTemplate = await this.productExtractor.GenerateProductsARMTemplateAsync(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup, singleApiName, apiTemplateResources, baseFilesGenerationDirectory, this.extractorParameters);
            Template productAPITemplate = await this.productApiExtractor.GenerateAPIProductsARMTemplateAsync(singleApiName, multipleApiNames, this.extractorParameters);
            Template apiTagTemplate = await this.apiTagExtractor.GenerateAPITagsARMTemplateAsync(singleApiName, multipleApiNames, this.extractorParameters);
            List<TemplateResource> productTemplateResources = productTemplate.Resources.ToList();
            List<TemplateResource> loggerResources = loggerTemplate.Resources.ToList();
            Template namedValueTemplate = await this.propertyExtractor.GenerateNamedValuesTemplateAsync(singleApiName, apiTemplateResources, this.extractorParameters, this.backendExtractor, loggerResources);
            Template tagTemplate = await this.tagExtractor.GenerateTagsTemplateAsync(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup, singleApiName, apiTemplateResources, productTemplateResources, this.extractorParameters.PolicyXMLBaseUrl, this.extractorParameters.PolicyXMLSasToken);
            List<TemplateResource> namedValueResources = namedValueTemplate.Resources.ToList();

            var backendResult = await this.backendExtractor.GenerateBackendsARMTemplateAsync(this.extractorParameters.SourceApimName, this.extractorParameters.ResourceGroup, singleApiName, apiTemplateResources, namedValueResources, this.extractorParameters);

            Dictionary<string, string> loggerResourceIds = null;
            if (this.extractorParameters.ParameterizeLogResourceId)
            {
                loggerResourceIds = LoggerTemplateUtils.GetAllLoggerResourceIds(loggerResources);
                loggerTemplate = LoggerTemplateUtils.SetLoggerResourceId(loggerTemplate);
            }

            // create parameters file
            Template templateParameters = await this.masterTemplateExtractor.CreateMasterTemplateParameterValues(apisToExtract, this.extractorParameters, apiLoggerId, loggerResourceIds, backendResult.Item2, namedValueResources);

            // write templates to output file location
            string apiFileName = FileNameGenerator.GenerateExtractorAPIFileName(singleApiName, this.extractorParameters.FileNames.BaseFileName);

            FileWriter.WriteJSONToFile(apiTemplate, string.Concat(baseFilesGenerationDirectory, apiFileName));
            // won't generate template when there is no resources
            if (!apiVersionSetTemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(apiVersionSetTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.ApiVersionSets));
            }
            if (!backendResult.Item1.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(backendResult.Item1, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.Backends));
            }
            if (!loggerTemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(loggerTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.Loggers));
            }
            if (!authorizationServerTemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(authorizationServerTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.AuthorizationServers));
            }
            if (!productTemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(productTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.Products));
            }
            if (!productAPITemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(productAPITemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.ProductAPIs));
            }
            if (!apiTagTemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(apiTagTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.ApiTags));
            }
            if (!tagTemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(tagTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.Tags));
            }
            if (!namedValueTemplate.Resources.IsNullOrEmpty())
            {
                FileWriter.WriteJSONToFile(namedValueTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.NamedValues));
            }

            if (this.extractorParameters.LinkedTemplatesBaseUrl != null)
            {
                // create a master template that links to all other templates
                Template masterTemplate = this.masterTemplateExtractor.GenerateLinkedMasterTemplate(
                    apiTemplate, globalServicePolicyTemplate, apiVersionSetTemplate, productTemplate, productAPITemplate,
                    apiTagTemplate, loggerTemplate, backendResult.Item1, authorizationServerTemplate, namedValueTemplate,
                    tagTemplate, this.extractorParameters.FileNames, apiFileName, this.extractorParameters);

                FileWriter.WriteJSONToFile(masterTemplate, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.LinkedMaster));
            }

            // write parameters to outputLocation
            FileWriter.WriteJSONToFile(templateParameters, string.Concat(baseFilesGenerationDirectory, this.extractorParameters.FileNames.Parameters));
        }

        /// <summary>
        /// Generates an api dictionary with apiName/versionsetName (if exist one) as key, list of apiNames as value
        /// </summary>
        /// <returns></returns>
        async Task<Dictionary<string, List<string>>> GetAllAPIsDictionary(string sourceApim, string resourceGroup)
        {
            // pull all apis from service
            JToken[] apis = await this.apiExtractor.GetAllApiObjsAsync(sourceApim, resourceGroup);

            // Generate folders based on all apiversionset
            var apiDictionary = new Dictionary<string, List<string>>();
            foreach (JToken oApi in apis)
            {
                string apiDisplayName = ((JValue)oApi["properties"]["displayName"]).Value.ToString();
                if (!apiDictionary.ContainsKey(apiDisplayName))
                {
                    List<string> apiVersionSet = new List<string>();
                    apiVersionSet.Add(((JValue)oApi["name"]).Value.ToString());
                    apiDictionary[apiDisplayName] = apiVersionSet;
                }
                else
                {
                    apiDictionary[apiDisplayName].Add(((JValue)oApi["name"]).Value.ToString());
                }
            }
            return apiDictionary;
        }

        async Task<Dictionary<string, object>> GetAllReferencedLoggers(List<string> apisToExtract, ExtractorParameters extractorParameters)
        {
            var apiLoggerId = new Dictionary<string, object>();

            var serviceDiagnostics = await this.apiExtractor.GetServiceDiagnosticsAsync(extractorParameters.SourceApimName, extractorParameters.ResourceGroup);
            JObject oServiceDiagnostics = JObject.Parse(serviceDiagnostics);

            var serviceloggerIds = new Dictionary<string, string>();
            foreach (var serviceDiagnostic in oServiceDiagnostics["value"])
            {
                string diagnosticName = ((JValue)serviceDiagnostic["name"]).Value.ToString();
                string loggerId = ((JValue)serviceDiagnostic["properties"]["loggerId"]).Value.ToString();
                apiLoggerId.Add(ParameterNamingHelper.GenerateValidParameterName(diagnosticName, ParameterPrefix.Diagnostic), loggerId);
            }


            foreach (string curApiName in apisToExtract)
            {
                var loggerIds = new Dictionary<string, string>();
                var diagnostics = await this.apiExtractor.GetApiDiagnosticsAsync(extractorParameters.SourceApimName, extractorParameters.ResourceGroup, curApiName);
                JObject oDiagnostics = JObject.Parse(diagnostics);
                foreach (var diagnostic in oDiagnostics["value"])
                {
                    string diagnosticName = ((JValue)diagnostic["name"]).Value.ToString();
                    string loggerId = ((JValue)diagnostic["properties"]["loggerId"]).Value.ToString();
                    loggerIds.Add(ParameterNamingHelper.GenerateValidParameterName(diagnosticName, ParameterPrefix.Diagnostic), loggerId);
                }
                if (loggerIds.Count != 0)
                {
                    apiLoggerId.Add(ParameterNamingHelper.GenerateValidParameterName(curApiName, ParameterPrefix.Api), loggerIds);
                }
            }

            return apiLoggerId;
        }
    }
}