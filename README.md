# ReliableCollectionsMissingTypesTool

Imagine a replica is upgraded from version 1 (v1) to version 2 (v2) where v2 includes a reliable dictionary whose key/value could be a type that does not exist in v1. When this upgraded replica communicates with other non-upgraded replicas (e.g. when add operation is performed on the dictionary) about this newly introduced type, non-upgraded replicas crash due to the absence of this new type within them.

To prevent getting into this issue, we could do a 2-phase upgrade which involves the following steps:
1. First-phase upgrade: Deploy a intermediate code package that introduces the new type but does not contain code that executes it and wait for all the replicas to upgrade to this intermediate version.
2. Second-phase upgrade: Deploy the desired code package that contains the new type and also the code that executes it.
Given two service manifest files corresponding to v1 and v2, this tool checks if types from v1 include all reliable collection types from v2.

Instructions on running this tool:
1. Download the attached zip folder and extract its content.
2. Navigate to the "Release" folder.
3. Run this command
HandlingMissingTypes.exe "<service_manifest_file_version1>" "<service_manifest_file_version2>"

Read more about this here https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-application-upgrade-data-serialization
