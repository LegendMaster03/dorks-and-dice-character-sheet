using System.Text.Json;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.UnitTests;

public sealed class RulesCoreGatewayContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CurrentMechanicsCatalogJsonPreservesCompetencyRelationshipAndSourceMetadata()
    {
        const string json = """
        {
          "scope": "global",
          "campaignId": null,
          "revisionNumber": 12,
          "publishedAt": "2026-09-18T12:00:00Z",
          "mechanics": [{
            "mechanicKey": "competency.skill.hide",
            "kind": "competency",
            "displayName": "Hide",
            "conceptKey": "skill.hide",
            "isAvailableUnderRuleset": true,
            "applicability": { "kind": "always", "requiresCharacterState": true, "requiredCapabilityKeys": [], "sourcePackageKey": null },
            "evaluationKind": "competency-profile",
            "canEvaluate": true,
            "inputs": [],
            "relationships": [{
              "relationshipKey": "stealth",
              "kind": "composite",
              "parentMechanicKey": "competency.skill.stealth",
              "componentMechanicKeys": ["competency.skill.hide", "competency.skill.move-silently"],
              "composition": "average-floor",
              "direction": "components-to-parent",
              "effectiveResolutionKind": "derive-parent",
              "canResolve": true,
              "missingMechanicKeys": []
            }],
            "booleanRequirements": [],
            "check": null,
            "competency": {
              "competencyKind": "skill",
              "familyName": null,
              "specialty": null,
              "governingAbilityKey": "dexterity",
              "supportsRanks": true,
              "supportsClassSkillState": true,
              "supportsTrainingState": false,
              "trainedOnly": false,
              "armorCheckPenaltyApplies": true,
              "defaultProfileSourceEntityRevisionId": "11111111-1111-1111-1111-111111111111",
              "profiles": [{
                "sourceEntityRevisionId": "11111111-1111-1111-1111-111111111111",
                "profileKey": "3x-ranked",
                "requiredCapabilityKeys": [],
                "competencyKind": "skill",
                "familyName": null,
                "specialty": null,
                "governingAbilityKey": "dexterity",
                "supportsRanks": true,
                "supportsClassSkillState": true,
                "supportsTrainingState": false,
                "trainedOnly": false,
                "armorCheckPenaltyApplies": true,
                "evaluationProfileKey": "3x-ranked",
                "evaluationKind": "sum",
                "canEvaluate": true,
                "inputs": [],
                "booleanRequirements": [],
                "gameEdition": "3.5e"
              }]
            },
            "contributorGroups": [],
            "sourceAttributions": [{
              "packageKey": "srd",
              "packageDisplayName": "SRD",
              "provider": "Wizards",
              "sourceCode": "PHB",
              "sourceRevisionNumber": 1,
              "workKey": "srd",
              "workDisplayName": "System Reference Document",
              "gameEdition": "3.5e",
              "releaseKind": "book",
              "publicationDate": "2003-07-01",
              "referenceKey": "srd",
              "referenceTitle": "SRD",
              "referenceUri": "https://example.test/srd",
              "presentationRequired": false,
              "referenceLinkRequired": false
            }]
          }]
        }
        """;

        var catalog = JsonSerializer.Deserialize<RulesCoreMechanicsCatalogView>(json, JsonOptions);
        Assert.NotNull(catalog);
        var mechanic = Assert.Single(catalog.Mechanics);
        Assert.Equal("skill.hide", mechanic.ConceptKey);
        Assert.True(mechanic.Competency!.SupportsRanks);
        Assert.Equal("derive-parent", Assert.Single(mechanic.Relationships).EffectiveResolutionKind);
        Assert.Equal("System Reference Document", Assert.Single(mechanic.SourceAttributions).WorkDisplayName);
        Assert.Equal("3.5e", Assert.Single(mechanic.Competency.Profiles).GameEdition);
    }

    [Fact]
    public void CurrentMechanicsCatalogJsonPreservesCompetencyFamiliesFacetsAndToolRelationships()
    {
        const string json = """
        {
          "scope": "global",
          "campaignId": null,
          "revisionNumber": 13,
          "publishedAt": "2026-09-22T19:28:21Z",
          "mechanics": [{
            "mechanicKey": "competency.skill.alchemy",
            "kind": "competency",
            "displayName": "Alchemy",
            "conceptKey": "skill.alchemy",
            "isAvailableUnderRuleset": true,
            "applicability": { "kind": "always", "requiresCharacterState": true, "requiredCapabilityKeys": [], "sourcePackageKey": null },
            "evaluationKind": "competency-profile",
            "canEvaluate": true,
            "inputs": [],
            "relationships": [],
            "booleanRequirements": [],
            "check": null,
            "competency": {
              "competencyKind": "specialized-skill",
              "familyName": "Craft",
              "specialty": "alchemy",
              "governingAbilityKey": "intelligence",
              "supportsRanks": true,
              "supportsClassSkillState": true,
              "supportsTrainingState": true,
              "trainedOnly": false,
              "armorCheckPenaltyApplies": false,
              "defaultProfileSourceEntityRevisionId": null,
              "profiles": [],
              "identityKey": "competency.alchemy",
              "identityName": "Alchemy",
              "sharedTrainingKey": "training.alchemy",
              "isFamily": false,
              "facets": [{
                "facetType": "skill",
                "profileSourceEntityRevisionIds": [],
                "supportsRanks": true,
                "supportsClassSkillState": true,
                "supportsTrainingState": true,
                "mechanicKeys": ["competency.skill.alchemy"]
              }, {
                "facetType": "tool",
                "profileSourceEntityRevisionIds": [],
                "supportsRanks": false,
                "supportsClassSkillState": false,
                "supportsTrainingState": true,
                "mechanicKeys": ["competency.tool.alchemists-supplies"]
              }],
              "relatedCompetencies": [{
                "kind": "related-competency",
                "targetType": "tool",
                "targetName": "Alchemist's Supplies",
                "scope": null,
                "sharesTrainingState": true
              }]
            },
            "contributorGroups": [],
            "sourceAttributions": []
          }]
        }
        """;

        var catalog = JsonSerializer.Deserialize<RulesCoreMechanicsCatalogView>(json, JsonOptions);

        Assert.NotNull(catalog);
        var competency = Assert.Single(catalog.Mechanics).Competency;
        Assert.NotNull(competency);
        Assert.Equal("competency.alchemy", competency.IdentityKey);
        Assert.Equal("training.alchemy", competency.SharedTrainingKey);
        Assert.False(competency.IsFamily);
        Assert.Equal(2, competency.Facets!.Count);
        Assert.Contains(competency.Facets, value => value.FacetType == "tool");
        var relationship = Assert.Single(competency.RelatedCompetencies!);
        Assert.Equal("Alchemist's Supplies", relationship.TargetName);
        Assert.True(relationship.SharesTrainingState);
    }

    [Fact]
    public void CurrentBatchEvaluationJsonPreservesCompetencyBreakdown()
    {
        const string json = """
        {
          "scope": "global",
          "campaignId": null,
          "revisionNumber": 12,
          "publishedAt": "2026-09-18T12:00:00Z",
          "evaluations": [{
            "mechanicKey": "competency.skill.hide",
            "evaluation": {
              "mechanicKey": "competency.skill.hide",
              "evaluationKind": "competency-profile",
              "value": 9,
              "target": null,
              "meetsTarget": null,
              "requirementsSatisfied": true,
              "unsatisfiedRequirementKeys": [],
              "appliedRollRules": [],
              "contributorGroups": [],
              "competencyBreakdown": { "abilityContribution": 3, "competencyContribution": 6 },
              "competencyProfileSourceEntityRevisionId": "11111111-1111-1111-1111-111111111111"
            }
          }]
        }
        """;

        var result = JsonSerializer.Deserialize<RulesCoreMechanicsBatchEvaluationView>(json, JsonOptions);
        Assert.NotNull(result);
        var evaluation = Assert.Single(result.Evaluations).Evaluation;
        Assert.NotNull(evaluation);
        Assert.Equal(3, evaluation.CompetencyBreakdown!.AbilityContribution);
        Assert.Equal(6, evaluation.CompetencyBreakdown.CompetencyContribution);
    }

    [Fact]
    public void CurrentResolvedRuleJsonProjectsStableAdvancementMetadataWithoutParsingSourceDocument()
    {
        const string json = """
        {
          "ruleConceptId": "11111111-1111-1111-1111-111111111111",
          "conceptKey": "position.acquisitions-documancer",
          "entityType": "charoption",
          "displayName": "Documancer",
          "rulesetRevisionNumber": 4,
          "rulesetFingerprint": "ruleset",
          "rulesetPublishedAt": "2026-09-18T12:00:00Z",
          "globalRuleDecisionId": "22222222-2222-2222-2222-222222222222",
          "globalDecisionNumber": 1,
          "decisionKind": "select",
          "decisionNote": null,
          "sourceEntityId": "33333333-3333-3333-3333-333333333333",
          "sourceEntityRevisionId": "44444444-4444-4444-4444-444444444444",
          "sourceRevisionNumber": 3,
          "sourceFingerprint": "source",
          "sourceEntityName": "Documancer",
          "sourceCode": "AI",
          "packageKey": "fixture",
          "packageDisplayName": "Fixture Package",
          "workKey": "acquisitions-incorporated",
          "workDisplayName": "Acquisitions Incorporated",
          "editionKey": "5e",
          "editionDisplayName": "5e",
          "contributions": [],
          "document": { "publisherNativeField": "ignored" }
        }
        """;

        var rule = JsonSerializer.Deserialize<RulesCoreResolvedRuleSummaryView>(json, JsonOptions);

        Assert.NotNull(rule);
        Assert.Equal("position.acquisitions-documancer", rule.ConceptKey);
        Assert.Equal("charoption", rule.EntityType);
        Assert.Equal("Acquisitions Incorporated", rule.WorkDisplayName);
    }
}
