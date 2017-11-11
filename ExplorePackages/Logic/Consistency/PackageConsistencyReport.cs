﻿namespace Knapcode.ExplorePackages.Logic
{
    public class PackageConsistencyReport : IConsistencyReport
    {
        public PackageConsistencyReport(
            PackageQueryContext context,
            bool isConsistent,
            V2ConsistencyReport v2,
            PackagesContainerConsistencyReport packagesContainer,
            FlatContainerConsistencyReport flatContainer,
            RegistrationConsistencyReport registrationOriginal,
            RegistrationConsistencyReport registrationGzipped,
            RegistrationConsistencyReport registrationSemVer2,
            SearchConsistencyReport search)
        {
            Context = context;
            IsConsistent = isConsistent;
            V2 = v2;
            PackagesContainer = packagesContainer;
            FlatContainer = flatContainer;
            RegistrationOriginal = registrationOriginal;
            RegistrationGzipped = registrationGzipped;
            RegistrationSemVer2 = registrationSemVer2;
            Search = search;
        }

        public PackageQueryContext Context { get; }
        public bool IsConsistent { get; }
        public V2ConsistencyReport V2 { get; }
        public PackagesContainerConsistencyReport PackagesContainer { get; }
        public FlatContainerConsistencyReport FlatContainer { get; }
        public RegistrationConsistencyReport RegistrationOriginal { get; }
        public RegistrationConsistencyReport RegistrationGzipped { get; }
        public RegistrationConsistencyReport RegistrationSemVer2 { get; }
        public SearchConsistencyReport Search { get; }
    }
}
