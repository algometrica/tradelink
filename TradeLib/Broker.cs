using System;
using System.Collections.Generic;
using TradeLib;

namespace TradeLib
{

    /// <summary>
    /// A simulated broker class for TradeLink.
    /// </summary>
    public class Broker
    {
        /// <summary>
        /// Occurs when [got order cancel].
        /// </summary>
        public event IntDelegate GotOrderCancel;
        /// <summary>
        /// Occurs when [got tick].
        /// </summary>
        public event TickDelegate GotTick;
        /// <summary>
        /// Occurs when [got order].
        /// </summary>
        public event OrderDelegate GotOrder;
        /// <summary>
        /// Occurs when [got fill].
        /// </summary>
        public event FillDelegate GotFill;
        /// <summary>
        /// Occurs when [got warning].  This will happen if an invalid order is received.
        /// </summary>
        public event DebugDelegate GotWarning;
        public Broker() 
        {
            Reset();

        }
        protected Account DEFAULT = new Account("DEFAULT","Defacto account when account not provided");
        protected Dictionary<string, List<Order>> MasterOrders = new Dictionary<string, List<Order>>();
        protected Dictionary<string, List<Trade>> MasterTrades = new Dictionary<string, List<Trade>>();
        protected List<Order> Orders { get { return MasterOrders[DEFAULT.ID]; } set { MasterOrders[DEFAULT.ID] = value; } }
        protected List<Trade> FillList { get { return MasterTrades[DEFAULT.ID]; } set { MasterTrades[DEFAULT.ID] = value; } }
        uint _nextorderid = 1;

        BBO TickToBBO(Tick t)
        {
            BBO book;
            book.bid = t.bid;
            book.bsize = t.BidSize;
            book.osize = t.AskSize;
            book.ask = t.ask;
            return book;
        }

        Tick BBOtoTick(string symbol,BBO bbo)
        {
            Tick t = new Tick(symbol);
            t.bid = bbo.bid;
            t.BidSize = bbo.bsize;
            t.ask = bbo.ask;
            t.AskSize = bbo.osize;
            return t;
        }
        protected void AddOrder(Order o,Account a) 
        {
            if (!a.isValid) throw new Exception("Invalid account provided"); // account must be good
            if (!MasterOrders.ContainsKey(a.ID))  // see if we have a blotter for this account
                MasterOrders.Add(a.ID,new List<Order>()); // if not,c reate one
            o.Account = a.ID; // make sure order knows his account
            if (o.id == 0) // if order id isn't set, set it
                o.id = _nextorderid++;
            MasterOrders[a.ID].Add(o); // record the order
            // if it's not a limit order we're done
            if (!o.isLimit) return;
            BBO book; // setup our book
            bool hasbook = bbodict.TryGetValue(o.symbol, out book); // see if we have book for this stock
            if (!hasbook) bbodict.Add(o.symbol,book);
            if (o.side) // do buy side of book
            {
                if ((book.bid == 0) || (book.bid <= o.price)) // see if we have first or better price
                {
                    book.bid = o.price; // update price
                    book.bsize += o.UnSignedSize; // if price was same them update size
                    if (GotTick != null) GotTick(BBOtoTick(o.symbol,book));
                }
            }
            else // now do same for sell side
            {
                if ((book.ask == 0) || (book.ask >= o.price))
                {
                    book.ask = o.price;
                    book.osize += o.UnSignedSize;
                    if (GotTick != null) GotTick(BBOtoTick(o.symbol,book));
                }
            }
            bbodict[o.symbol] = book; // now book is up-to-date
        }
        public bool CancelOrder(long orderid)
        {
            foreach (string a in MasterOrders.Keys) // go through every account
            {
                for (int i = 0; i < MasterOrders[a].Count; i++) // and every order
                    if (MasterOrders[a][i].id == (int)orderid) // if we have order with requested id
                    {
                        if (GotOrderCancel != null) //send cancel notifcation to any subscribers
                            GotOrderCancel(orderid); 
                        MasterOrders[a].RemoveAt(i); // remove/cancel order
                        return true;
                    }
            }
            return false;
        }
        /// <summary>
        /// Sends the order to the broker. (uses the default account)
        /// </summary>
        /// <param name="o">The order to be send.</param>
        /// <returns>true if the order was accepted.</returns>
        public bool sendOrder(Order o) 
        { 
            if (o.Account=="")
                return sendOrder(o, DEFAULT);
            return sendOrder(o, new Account(o.Account));

        }
        /// <summary>
        /// Sends the order to the broker for a specific account.
        /// </summary>
        /// <param name="o">The order to be sent.</param>
        /// <param name="a">the account to send with the order.</param>
        /// <returns>order id if order was accepted, zero otherwise</returns>
        public int sendOrder(Order o,Account a)
        {
            if ((!o.isValid) || (!a.isValid))
            {
                if (GotWarning != null)
                    GotWarning(!o.isValid ? "Invalid order: " + o.ToString() : "Invalid Account" + a.ToString());
                return 0;
            }
            AddOrder(o, a);
            if (GotOrder != null) GotOrder(o);
            return o.id;
        }
        // return existing tick if tick is better than BBO
        // leave it, otherwise return bbo
        void UpdateBook(Tick incomingtick)
        {
            // if it's a trade, no book to update... just fire the tick
            if (incomingtick.isTrade) { if (GotTick != null) GotTick(incomingtick); return; }
            BBO currentbook;
            // if we don't have a book for this stock, create one and fire the tick
            if (!bbodict.TryGetValue(incomingtick.sym, out currentbook))
            {
                if (GotTick != null) GotTick(incomingtick);
                bbodict.Add(incomingtick.sym, TickToBBO(incomingtick));
                return;
            }
            // whats left is some book is present
            // 
            
        }

        Dictionary<string,BBO> bbodict = new Dictionary<string,BBO>();
        struct BBO
        {
            public decimal bid;
            public decimal ask;
            public int bsize;
            public int osize;
            public bool hasAsk { get { return ask*osize!=0; } }
            public bool hasBid { get { return bid * bsize != 0; } }
        }
        /// <summary>
        /// Executes any open orders allowed by the specified tick.
        /// </summary>
        /// <param name="tick">The tick.</param>
        /// <returns>the number of orders executed using the tick.</returns>
        public int Execute(Tick tick)
        {
            UpdateBook(tick);
            if (!tick.isTrade) return 0;
            int availablesize = (int)Math.Abs(tick.size);
            int max = this.Orders.Count;
            int filledorders = 0;
            foreach (string a in MasterOrders.Keys)
            { // go through each account
                for (int i = 0; i < MasterOrders[a].Count; i++)
                { // go through each order
                    Order o = MasterOrders[a][i];
                    if (tick.sym != o.symbol) continue; //make sure tick is for the right stock
                    int mysize = (int)Math.Abs(o.size);
                    if (((mysize <= availablesize) && (o.price == 0) && (o.stopp == 0)) || //market order
                        (o.side && (mysize <= availablesize) && (tick.trade <= o.price) && (o.stopp == 0)) || // buy limit
                        (!o.side && (mysize <= availablesize) && (tick.trade >= o.price) && (o.stopp == 0)) || //sell limit
                        (o.side && (mysize <= availablesize) && (tick.trade >= o.stopp) && (o.price == 0)) || // buy stop
                        (!o.side && (mysize <= availablesize) && (tick.trade <= o.stopp) && (o.price == 0))) // sell stop
                    { // sort filled trades by symbol
                        MasterOrders[a].RemoveAt(i);
                        if (!MasterTrades.ContainsKey(a)) MasterTrades.Add(a, new List<Trade>());
                        o.Fill(tick); // fill our trade
                        availablesize -= mysize; // don't let other trades fill on same tick
                        MasterTrades[a].Add((Trade)o); // record trade
                        if (GotFill != null) GotFill((Trade)o); // notify subscribers after recording trade
                        filledorders++; // count the trade
                    }
                }
            }
            return filledorders;
        }

        /// <summary>
        /// Resets this instance, clears all orders/trades/accounts held by the broker.
        /// </summary>
        public void Reset()
        {
            MasterOrders.Clear();
            MasterTrades.Clear();
            MasterOrders.Add(DEFAULT.ID, new List<Order>());
            MasterTrades.Add(DEFAULT.ID, new List<Trade>());
        }
        public void CancelOrders() { CancelOrders(DEFAULT); bbodict.Clear(); }
        public void CancelOrders(Account a) { MasterOrders[a.ID].Clear(); }
        /// <summary>
        /// Gets the complete execution list for this account
        /// </summary>
        /// <param name="a">account to request blotter from.</param>
        /// <returns></returns>
        public List<Trade> GetTradeList(Account a) { return MasterTrades[a.ID]; }
        /// <summary>
        /// Gets the list of open orders for this account.
        /// </summary>
        /// <param name="a">Account.</param>
        /// <returns></returns>
        public List<Order> GetOrderList(Account a) { return MasterOrders[a.ID]; }
        public List<Trade> GetTradeList() { return GetTradeList(DEFAULT); }
        public List<Order> GetOrderList() { return GetOrderList(DEFAULT); }

        /// <summary>
        /// Gets the open positions for the default account.
        /// </summary>
        /// <param name="symbol">The symbol to get a position for.</param>
        /// <returns>current position</returns>
        public Position GetOpenPosition(string symbol) { return GetOpenPosition(symbol, DEFAULT); }
        /// <summary>
        /// Gets the open position for the specified account.
        /// </summary>
        /// <param name="symbol">The symbol to get a position for.</param>
        /// <param name="a">the account.</param>
        /// <returns>current position</returns>
        public Position GetOpenPosition(string symbol,Account a)
        {
            Position pos = new Position(symbol);
            if (!MasterTrades.ContainsKey(a.ID)) return pos;
            foreach (Trade trade in MasterTrades[a.ID]) 
                if (trade.symbol==symbol) 
                    pos.Adjust(trade);
            return pos;
        }

        /// <summary>
        /// Gets the closed PL for a particular symbol and brokerage account.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="a">The Account.</param>
        /// <returns>Closed PL</returns>
        public decimal GetClosedPL(string symbol, Account a)
        {
            Position pos = new Position(symbol);
            decimal pl = 0;
            foreach (Trade trade in MasterTrades[a.ID])
            {
                if (trade.symbol == pos.Symbol)
                    pl += pos.Adjust(trade);
            }
            return pl;
        }

        /// <summary>
        /// Gets the closed PL for a particular symbol on the default account.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <returns></returns>
        public decimal GetClosedPL(string symbol) { return GetClosedPL(symbol, DEFAULT); }
        /// <summary>
        /// Gets the closed PL for an entire account. (all symbols)
        /// </summary>
        /// <param name="a">The account.</param>
        /// <returns>Closed PL</returns>
        public decimal GetClosedPL(Account a)
        {
            Dictionary<string, Position> poslist = new Dictionary<string, Position>();
            Dictionary<string,decimal> pllist = new Dictionary<string,decimal>();
            foreach (Trade trade in MasterTrades[a.ID])
            {
                if (!poslist.ContainsKey(trade.symbol))
                {
                    poslist.Add(trade.symbol, new Position(trade.symbol));
                    pllist.Add(trade.symbol, 0);
                }
                pllist[trade.symbol] += poslist[trade.symbol].Adjust(trade);
            }
            decimal pl = 0;
            foreach (string sym in pllist.Keys)
                pl += pllist[sym];
            return pl;
        }
        /// <summary>
        /// Gets the closed PL for all symbols on the default account.
        /// </summary>
        /// <returns>Closed PL</returns>
        public decimal GetClosedPL() { return GetClosedPL(DEFAULT); }

        /// <summary>
        /// Gets the closed points (points = PL on per-share basis) for given symbol/account.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="account">The account.</param>
        /// <returns>points</returns>
        public decimal GetClosedPT(string symbol, Account account)
        {
            Position pos = new Position(symbol);
            decimal points = 0;
            foreach (Trade t in MasterTrades[account.ID])
            {
                points += BoxMath.ClosePT(pos, t);
                pos.Adjust(t);
            }
            return points;
        }
        /// <summary>
        /// Gets the closed PT/Points for given symbol on default account.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <returns></returns>
        public decimal GetClosedPT(string symbol) { return GetClosedPT(symbol, DEFAULT); }
        /// <summary>
        /// Gets the closed Points on a specific account, all symbols.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <returns></returns>
        public decimal GetClosedPT(Account account)
        {
            Dictionary<string, Position> poslist = new Dictionary<string, Position>();
            Dictionary<string, decimal> ptlist = new Dictionary<string, decimal>();
            foreach (Trade trade in MasterTrades[account.ID])
            {
                if (!poslist.ContainsKey(trade.symbol))
                {
                    poslist.Add(trade.symbol, new Position(trade.symbol));
                    ptlist.Add(trade.symbol, 0);
                }
                ptlist[trade.symbol] += BoxMath.ClosePT(poslist[trade.symbol], trade);
                poslist[trade.symbol].Adjust(trade);
            }
            decimal points = 0;
            foreach (string sym in ptlist.Keys)
                points += ptlist[sym];
            return points;

        }
        /// <summary>
        /// Gets the closed Points on the default account.
        /// </summary>
        /// <returns></returns>
        public decimal GetClosedPT() { return GetClosedPT(DEFAULT); }



    }

    public enum Brokers
    {
        Unknown = -1,
        TradeLinkSimulation = 0,
        Assent,
        InteractiveBrokers,
        Genesis,
        Bright,
        Echo,
    }

}
