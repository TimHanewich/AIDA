using System;
using System.Collections.Generic;
using SecuritiesExchangeCommission.Edgar;
using SecuritiesExchangeCommission.Edgar.Data;
using System.Collections.Generic;

namespace AIDA.Finance
{
    //Manages API calls for data to the SEC API so we aren't constantly calling to it (it is a lot of data to pull down each time)
    public class SECBandwidthManager
    {
        //SHARED INSTANCE
        private static SECBandwidthManager _Instance = new SECBandwidthManager();
        public static SECBandwidthManager Instance
        {
            get
            {
                return _Instance;
            }
        }


        private static List<CompanyFactsQuery> AllCompanyFactsQueries;

        public SECBandwidthManager()
        {
            AllCompanyFactsQueries = new List<CompanyFactsQuery>();
        }

        public static async Task<CompanyFactsQuery> CompanyFactsQueryAsync(int cik)
        {
            //Search if we have one
            foreach (CompanyFactsQuery existing_cfq in AllCompanyFactsQueries)
            {
                if (existing_cfq.CIK == cik)
                {
                    return existing_cfq;
                }
            }

            //We must not have one, so get it
            CompanyFactsQuery cfq = await CompanyFactsQuery.QueryAsync(cik);
            AllCompanyFactsQueries.Add(cfq);
            return cfq;
        }


    }
}