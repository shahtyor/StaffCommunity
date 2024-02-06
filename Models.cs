using System;
using System.Collections.Generic;

namespace StaffCommunity
{
    public class telegram_user
    {
        public long id { get; set; }
        public DateTime first_use { get; set; }
        public string own_ac { get; set; }
        public bool is_reporter { get; set; }
        public bool is_requestor { get; set; }
        public string permitted_ac { get; set; }
        public ExtendedResult exres { get; set; }
        public sign_in Token { get; set; }
    }

    public class sign_in
    {
        public short type { get; set; }
        public string id_user { get; set; }
    }

    public class PermittedAC
    {
        public string Code { get; set; }
        public string Permit { get; set; }
    }

    public enum RType
    {
        Red = 1,
        Yellow = 2,
        Green = 3
    }

    public enum GetNonDirectType
    {
        Off = 0,
        On = 1,
        Auto = 2
    }

    public enum PlaceType
    {
        Economy = 0,
        Business = 1,
        SA = 2
    }

    public class ExtendedResult
    {
        public List<Flight> DirectRes { get; set; }
        public AviasalesInfo DirectInfo { get; set; }
        public List<TransferPoint> TransferPoints { get; set; }
        public List<NonDirectResult> NonDirectRes { get; set; }
        public List<TransferPoint> AirportsOrigin { get; set; }
        public List<TransferPoint> AirportsDestination { get; set; }
        public List<TransferPoint> ResultTransferPoints { get; set; }
        public string Log { get; set; }
        public string Alert { get; set; }
    }

    public class Flight
    {
        /// <summary>
        /// Код аэропорта вылета
        /// </summary>
        public string Origin { get; set; }                       //+
        /// <summary>
        /// Код аэропорта прилета
        /// </summary>
        public string Destination { get; set; }                  //+

        public string DepartureName { get; set; }
        /// <summary>
        /// Терминал вылета
        /// </summary>
        public string DepartureTerminal { get; set; }            //+

        public string DepartureCity { get; set; }

        public string DepartureCityName { get; set; }

        public string ArrivalName { get; set; }
        /// <summary>
        /// Терминал прилета
        /// </summary>
        public string ArrivalTerminal { get; set; }              //+

        public string ArrivalCity { get; set; }

        public string ArrivalCityName { get; set; }

        public int? OriginDistance { get; set; }

        public int? DestinationDistance { get; set; }
        /// <summary>
        /// Номер рейса
        /// </summary>
        public string FlightNumber { get; set; }                 //+
        /// <summary>
        /// Оперирующая авиакомпания
        /// </summary>
        public string OperatingCarrier { get; set; }             //+
        /// <summary>
        /// Маркетинговая авиакомпания
        /// </summary>
        public string MarketingCarrier { get; set; }             //+

        public string OperatingName { get; set; }

        public string MarketingName { get; set; }
        /// <summary>
        /// Дата и время вылета
        /// </summary>
        public DateTime DepartureDateTime { get; set; }          //+
        /// <summary>
        /// Дата и время прилета
        /// </summary>
        public DateTime ArrivalDateTime { get; set; }            //+
        /// <summary>
        /// Тип самолета
        /// </summary>
        public string Equipment { get; set; }                    //+

        public string EquipmentName { get; set; }
        /// <summary>
        /// Время в пути в минутах
        /// </summary>
        public int Duration { get; set; }                        //+
        /// <summary>
        /// Список классов бронирования с количеством мест (L4, K5, M3, …)
        /// </summary>
        public string[] NumSeatsForBookingClass { get; set; }    //+

        /// <summary>
        /// Количество  SA пассажиров
        /// </summary>
        public int? CntSAPassenger { get; set; }

        public int Sum_subclass_variant { get; set; }
        public int EconomyPlaces { get; set; }
        public int BusinessPlaces { get; set; }
        public string AllPlaces { get; set; }

        public RType Rating { get; set; }

        /// <summary>
        /// Прогноз загрузки
        /// </summary>
        public decimal Forecast { get; set; }

        /// <summary>
        /// Количество данных в статистике, на основании которых сделан прогноз
        /// </summary>
        public long C { get; set; }

        /// <summary>
        /// Точность прогноза
        /// </summary>
        public string Accuracy { get; set; }
    }

    public class AviasalesInfo
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Link { get; set; }
        public DateTime FoundAt { get; set; }
        public string Source { get; set; }
    }

    public class TransferPoint
    {
        /// <summary>
        /// Код аэропорта пересадки
        /// </summary>
        public string Origin { get; set; }                       //+

        public string Name { get; set; }

        public string City { get; set; }

        public string CityName { get; set; }

        public string Country { get; set; }

        public string CountryName { get; set; }

        public int? Distance { get; set; }
    }

    public class NonDirectResult
    {
        public string Transfer { get; set; }
        public List<Flight> To_airport_transfer { get; set; }
        public List<Flight> From_airport_transfer { get; set; }
        public AviasalesInfo ToTransferInfo { get; set; }
        public AviasalesInfo FromTransferInfo { get; set; }
        public int RedCount { get; set; }
        public int YellowCount { get; set; }
        public int GreenCount { get; set; }
        public DateTime FirstFlightTransfer { get; set; }
        public DateTime LastFlightTransfer { get; set; }
        public List<TransferPoint> AirportsOrigin { get; set; }
        public List<TransferPoint> AirportsDestination { get; set; }
        public string Log { get; set; }
        public string Alert { get; set; }
    }

    public class Request
    {
        public long Id { get; set; }
        public string Id_requestor { get; set; }
        public string Id_reporter { get; set; }
        public short Request_status { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public DateTime DepartureDateTime { get; set; }
        public string Number_flight { get; set; }
        public string Desc_fligth { get; set; }
        public int? Economy_count { get; set; }
        public int? Business_count { get; set; }
        public int? SA_count { get; set; }
        public short Source { get; set; }
        public string Push_id { get; set; }
    }

    public class TelMessage
    {
        public long ChatId { get; set; }
        public int MessageId { get; set; }
    }

    public class TokenCollection
    {
        public int SubscribeTokens { get; set; }
        public int NonSubscribeTokens { get; set; }
        public int DebtSubscribeTokens { get; set; }
        public int DebtNonSubscribeTokens { get; set; }
        public string Error { get; set; }
    }
}
