# Phase 4 multilingual desktop implementation plan

> Workers: use superpowers:subagent-driven-development; one implementation child at a time, read-only research/review may parallel. Persistent ledger wins over scratch deletion.

Goal: all-provider multilingual pipeline, domestic MT choices and modern usableWPF, keepingPhase3.
Architecture: Core owns neutralLanguageRegistry/settings/session semantics; Providers owns evidence-backedcodes/capabilities/factories/probes/fallback. WPF consumespubliccapabilityAPI, withreusabletheme/pickers/scrollbehavior. ExistingASR/MTcontracts andCoreFinalpriority retained.
Tech: existing.NET9/WPF/NAudio, officialAlibabaSDKonlyifnecessary, noUIframeworkmigration.
Spec: REQUIREMENTS.md. User explicitly requests direct autonomous development; no designapprovalquestion required.

## Global constraints
- Branchphase4-multilingual-ui from4e19fc5; original/client/r4/read-onlybaseline preserved; no push/merge.
- No realkeys/privateaudio read; explicitin-producttests only, offlinefixturesseparatefromcloud/hardwareacceptance.
- User supplementallproviders,languageflow,domesticMT,fallbackconsent andUI are binding, nothing silentlyremoved.
- SerialMSBuild/workspaceCLIhome, meaningfulRED/GREEN andtaskreview, boundedcancellation/queues andsecretbindingpreserved.

## Tasks
- [ ] 1. Auditandprimarydocsresearch: domesticAPIs/SDK/TokenHub/presets; versionedlegacycapabilitymaps. Rootauditcode/tests; writeAUDIT.md, DOMESTIC-SPECS.md, LANGUAGE-SPECS.md. No implementation childrenuntilsharedcontractsdefined.
- [ ] 2. Root unifiedregistry/settingsmigration/ProviderLanguages &runtimevalidation,mappingeveryexistingadapter,DeepLcachedlanguagequery. Testsrealrequestparams,pairrejection,aliases/auto/modelregion,oldsettingssecrets intact.
- [ ] 3. DomesticMT implementation: Baidu/AlibabaofficialSDK, safeerrors/signature/requestfixtures andcatalogfields. Root ownssharedcatalog/factory,workerisolatedadapters/tests. Reviewbeforemovingimplementationseat.
- [ ] 4. Root preset/connectionprobesandopt-inconsentedfallback; boundedroutes/errors/nohiddenpaidcalls, context/cacheidentityregression.
- [ ] 5. UI implementation: modernmain/settings/nav/themes,searchlanguages/recent/swap+actualstop/restart, dynamicprovider/help/testing/fallbackconsent, reusablewheelrelay. WPFfixturetests and actualwindowrendering; roothandlesbackendintegrationcontracts. Review.
- [ ] 6. Root allrequiredlocalhostpipelinecombos/parametercases/lateswitch+baselinegate. Freshwholebranchreview, scopedfixes/fullsuite, realWPF/nativewheelobserve whenpossible,newportable+ZIP+secrets/hash/preservationaudit, docs/progress/localcommits. Manualtouchpad/cloud/hoursoaktrackedunknown.

## Shared-file preflight
| Tasks | Shared surface | Rule |
|---|---|---|
|2/3/4|ProviderHTTP/catalog/capabilities|Root owns sharedfiles; domesticadapter consumesmappingAPI afterfreeze; no concurrentedits.|
|2/5|AppSettings/ProviderLanguages/Diagnostics|Freeze exactsignatures inUIbrief beforedispatch; UIvendor-neutral bydescriptor.|
|3/5|implementationseat/buildoutputs|Onlyonechildimplements; rootindependentbackendworkallowed; serialbuildslotcoordination.|
|all/6|generation/cancel/privacy/baseline|Existing36+19+26+86+36+26+13testsretained; no paidcalls/credentialreads.|

## Review focus
1. Languagechangeduringuncooperativeoldtranslationmustnotreappear; testtask2/5/6.
2. Provider switchpreservesinvalidlanguage visibly, neverdefaultEnglish; task2/5.
3. UnknownLLMcapabilityrequiresuserdeclaration/test, notimplicitalllanguages; task2/4/5.
4. Keydestinationchange/probe/fallbackmustnotforwardwrongprofileorsecret; task3/4/5.
5. Nestedwheelpopup/multilineedge/focus/DPIclip needsrouted-eventandactualWPFtests, hardwareunknownhonest; task5/6.
