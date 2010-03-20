﻿using System;
using System.Collections.Generic;
using TradeLink.API;
using TradeLink.Common;
using NUnit.Framework;

namespace TestTradeLink
{
    [TestFixture]
    public class TestAsyncResponse
    {
        public TestAsyncResponse()
        {
            ar.GotTick += new TickDelegate(ar_GotTick);
            ar.GotTickQueueEmpty += new VoidDelegate(ar_GotTickQueueEmpty);
            ar.GotImbalanceQueueEmpty += new VoidDelegate(ar_GotImbalanceQueueEmpty);
            ar.GotImbalance += new ImbalanceDelegate(ar_GotImbalance);
        }




        bool tickdone = false;
        void ar_GotTickQueueEmpty()
        {
            tickdone = tc >= MAXTICKS;
        }

        bool imbdone = false;
        void ar_GotImbalanceQueueEmpty()
        {
            imbdone = ic >= MAXIMBS;
        }


        const int MAXTICKS = 20000;
        const int MAXIMBS = 1000;
        const string SYM = "TST";
        AsyncResponse ar = new AsyncResponse();
        const int MAXWAITS = 100;

        [Test]
        public void TickTest()
        {
            // get ticks
            Tick[] sent = 
                TradeLink.Research.RandomTicks.GenerateSymbol(SYM, MAXTICKS);

            // send ticks
            for (int i = 0; i < sent.Length; i++)
                ar.newTick(sent[i]);

            int waits = 0;
            // wait for reception
            while (!tickdone)
            {
                System.Threading.Thread.Sleep(AsyncResponse.SLEEP);
                if (waits++ > 5)
                {
                    //System.Diagnostics.Debugger.Break();
                    Console.WriteLine(string.Format("waits: {0} tickcount: {1}", waits, tc));
                }
            }

            Assert.AreEqual(0, ar.TickOverrun,"tick overrun");

            // verify done
            Assert.IsTrue(tickdone, tc.ToString() + " ticks recv/"+MAXTICKS.ToString());


            //verify count
            Assert.AreEqual(MAXTICKS, tc);

            // verify order
            Assert.IsTrue(torder);
        }

        [Test]
        public void ImbalanceTest()
        {
            // get imbalances
            Random r =new Random((int)DateTime.Now.Ticks);
            List<Imbalance> sent = new List<Imbalance>();
            for (int i = 0; i < MAXIMBS; i++)
                sent.Add(new ImbalanceImpl(SYM, "NYSE", r.Next(-1000000, 1000000), i, 0, 0, 0));

            // send imbalances
            for (int i = 0; i < sent.Count; i++)
                ar.newImbalance(sent[i]);

            // wait for reception
            int waits = 0;
            // wait for reception
            while (!imbdone)
            {
                System.Threading.Thread.Sleep(AsyncResponse.SLEEP);
                if (waits++ > 5)
                {
                    //System.Diagnostics.Debugger.Break();
                    Console.WriteLine(string.Format("waits: {0} tickcount: {1}", waits, tc));
                }

            }

            //verify no overruns
            Assert.AreEqual(0, ar.ImbalanceOverrun,"imbalance overrun");

            // verify done
            Assert.IsTrue(imbdone, ic.ToString() + " imbalances recv/"+MAXIMBS.ToString());

            // verify count
            Assert.AreEqual(MAXIMBS, ic);

            // verify order
            Assert.IsTrue(iorder);

        }

        Tick[] trecv = new Tick[MAXTICKS];
        Imbalance[] irecv = new Imbalance[MAXIMBS];

        int ic = 0;
        int tc = 0;
        bool iorder = true;
        bool torder = true;
        int lastt = 0;
        int lasti = 0;

        void ar_GotImbalance(Imbalance imb)
        {
            try
            {
                bool v = (lasti <= imb.ThisTime);
                if (!v)
                    iorder = false;
                irecv[ic++] = imb;
                lasti = imb.ThisTime;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                ic++;
            }
        }

        void ar_GotTick(Tick t)
        {
            try
            {
                bool v = true;
                v = (lastt <= t.time);
                if (!v)
                    torder = false;
                trecv[tc++] = t;
                lastt = t.time;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                tc++;
            }

        }

        [TestFixtureTearDown]
        public void StopTest()
        {
            ar.Stop();
        }
    }
}
