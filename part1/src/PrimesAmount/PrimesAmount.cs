using System.Threading;
using System.Collections.Concurrent;

namespace async_tests{



    public class PrimesAmount
    {
        private readonly int border;
        private int count=0; 

        public PrimesAmount(int pborder)
        {
            if (pborder >0)
                border=pborder;
            else throw new ArgumentException("parameter should be a natural number");

        }

        // Zero is a valid result, including before the first calculation.
        public int Count => count;
        public int Border => border;


        public  ConcurrentQueue<int> CountPrimesAmount ( PrimeService pService )
        {
           ConcurrentQueue<int> primes = new();
            
            if( border>1 )
            {
                try{
                    Parallel.For(2, border,  i =>
                {
                    if(pService.CheckIsPrime(i))
                        
                        primes.Enqueue(i);
                });
                }

                catch(Exception)
                {
                    primes.Clear();//TODO
                }


                finally
                {
                    pService.ClearCache();
                }
                
            }     
            
            count = primes.Count;
            return primes;

        }



    }




}
