﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;

namespace neleo_com.Logic.Timing {

    /// <summary>
    ///   Logic module to find the ealiest of the input time.</summary>
    public class Wakeup : LogicNodeBase {

        /// <summary>
        ///   The Type Service manages incoming and outgoing ports.</summary>
        private readonly ITypeService TypeService;

        /// <summary>
        ///   The Scheduler Service manages access to the clock and scheduled callbacks.</summary>
        private readonly ISchedulerService SchedulerService;

        /// <summary>
        ///   Number of start date/time items (1..10).</summary>
        [Parameter(DisplayOrder = 1, IsRequired = true, IsDefaultShown = false)]
        public IntValueObject ItemCount {
            get; private set;
        }

        /// <summary>
        ///   Start times for appointments.</summary>
        [Input(DisplayOrder = 2, IsDefaultShown = true)]
        public IList<DateTimeValueObject> StartDateTime {
            get; private set;
        }

        /// <summary>
        ///   Lead time for appointments.</summary>
        [Parameter(DisplayOrder = 3, IsDefaultShown = true)]
        public TimeSpanValueObject LeadTime {
            get; private set;
        }

        /// <summary>
        ///   Number of holiday items (1..10).</summary>
        [Parameter(DisplayOrder = 4, IsRequired = true, IsDefaultShown = false)]
        public IntValueObject ItemCountHolidays {
            get; private set;
        }

        /// <summary>
        ///   Start dates for holidays.</summary>
        [Input(DisplayOrder = 5, IsDefaultShown = true)]
        public IList<DateTimeValueObject> StartDateTimeHolidays {
            get; private set;
        }

        /// <summary>
        ///   Treat a day as workday.</summary>
        [Parameter(DisplayOrder = 6, IsRequired = true, IsDefaultShown = false)]
        public IList<BoolValueObject> Workdays {
            get; private set;
        }

        /// <summary>
        ///   Switch to include/exclude default start time from calculation.</summary>
        [Input(DisplayOrder = 7, IsDefaultShown = false)]
        public BoolValueObject DefaultTimeEnabled {
            get; private set;
        }

        /// <summary>
        ///   Default start time (workdays).</summary>
        [Input(DisplayOrder = 8, IsDefaultShown = false)]
        public TimeSpanValueObject DefaultTime {
            get; private set;
        }

        /// <summary>
        ///   Default start time (holidays).</summary>
        [Input(DisplayOrder = 9, IsDefaultShown = false)]
        public TimeSpanValueObject DefaultTimeHolidays {
            get; private set;
        }

        /// <summary>
        ///   Minimal wakeup date and time.</summary>
        [Output(DisplayOrder = 1, IsDefaultShown = true)]
        public DateTimeValueObject WakeupDateTime {
            get; private set;
        }

        /// <summary>
        ///   Token for the scheduled update.</summary>
        private SchedulerToken UpdateToken {
            get; set;
        }

        /// <summary>
        ///   Constructor to setup the ports and services.</summary>
        /// <param name="context">
        ///   Context of the node instance to connect to services.</param>
        public Wakeup(INodeContext context) {

            // ensure context is set
            context.ThrowIfNull(nameof(context));

            // initializes services
            this.TypeService = context.GetService<ITypeService>();
            this.SchedulerService = context.GetService<ISchedulerService>();

            // initialize ports
            this.ItemCount = this.TypeService.CreateInt(PortTypes.Integer, nameof(this.ItemCount), 1);
            this.ItemCount.MinValue = 1;
            this.ItemCount.MaxValue = 10;

            this.StartDateTime = new List<DateTimeValueObject>();
            ListHelpers.ConnectListToCounter(this.StartDateTime, this.ItemCount,
                this.TypeService.GetValueObjectCreator(PortTypes.DateTime, nameof(this.StartDateTime)), null);

            this.LeadTime = this.TypeService.CreateTimeSpan(PortTypes.TimeSpan, nameof(this.LeadTime));
            this.LeadTime.MinValue = TimeSpan.Zero;
            this.LeadTime.MaxValue = new TimeSpan(23, 59, 59);
            if (!this.LeadTime.HasValue)
                this.LeadTime.Value = TimeSpan.FromHours(1);

            this.ItemCountHolidays = this.TypeService.CreateInt(PortTypes.Integer, nameof(this.ItemCountHolidays), 0);
            this.ItemCountHolidays.MinValue = 0;
            this.ItemCountHolidays.MaxValue = 25;

            this.StartDateTimeHolidays = new List<DateTimeValueObject>();
            ListHelpers.ConnectListToCounter(this.StartDateTimeHolidays, this.ItemCountHolidays,
                this.TypeService.GetValueObjectCreator(PortTypes.DateTime, nameof(this.StartDateTimeHolidays)), null);

            this.Workdays = new List<BoolValueObject>();
            ListHelpers.UpdateListLength(this.Workdays, 7,
                this.TypeService.GetValueObjectCreator(PortTypes.Binary, nameof(this.Workdays)), null);

            this.DefaultTimeEnabled = this.TypeService.CreateBool(PortTypes.Binary, nameof(this.DefaultTimeEnabled), false);

            this.DefaultTime = this.TypeService.CreateTimeSpan(PortTypes.Time, nameof(this.DefaultTime));
            this.DefaultTime.MinValue = TimeSpan.Zero;
            this.DefaultTime.MaxValue = new TimeSpan(23, 59, 59);

            this.DefaultTimeHolidays = this.TypeService.CreateTimeSpan(PortTypes.Time, nameof(this.DefaultTimeHolidays));
            this.DefaultTimeHolidays.MinValue = TimeSpan.Zero;
            this.DefaultTimeHolidays.MaxValue = new TimeSpan(23, 59, 59);

            this.WakeupDateTime = this.TypeService.CreateDateTime(PortTypes.DateTime, nameof(this.WakeupDateTime));

        }

        /// <summary>
        ///   Localizes all options and labels.</summary>
        /// <param name="language">
        ///   The language / culture.</param>
        /// <param name="key">
        ///   The key for the option or label.</param>
        /// <returns>
        ///   A localized key or label.</returns>
        public override String Localize(String language, String key) {

            // ensure that parameters are set
            language.ThrowIfNull(nameof(language));
            key.ThrowIfNull(nameof(key));

            // load culture
            CultureInfo culture;
            try {
                culture = CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException) {
                culture = CultureInfo.InvariantCulture;
            }

            // filter and map keys
            if (key.StartsWith(nameof(this.StartDateTimeHolidays))) {

                String identifier = key.Substring(nameof(this.StartDateTimeHolidays).Length);
                return String.Format(ResourceManager.GetString(nameof(this.StartDateTimeHolidays), culture) ?? key, identifier);

            }
            else if (key.StartsWith(nameof(this.StartDateTime))) {

                String identifier = key.Substring(nameof(this.StartDateTime).Length);
                return String.Format(ResourceManager.GetString(nameof(this.StartDateTime), culture) ?? key, identifier);

            }
            else {

                return ResourceManager.GetString(key, culture) ?? key;

            }

        }

        /// <summary>
        ///   Caluculates the output based on all available input variables.</summary>
        public override void Execute() {

            this.UpdateWakeupDateTime();

        }

        /// <summary>
        ///   Calculate the wakeup startup time and setup the hourly update schedule.</summary>
        private void UpdateWakeupDateTime() {

            DateTime localNow = this.SchedulerService.Now;
            ICollection<DateTime> starts = new Collection<DateTime>();

            // add default wakeup time if enabled
            Boolean defaultTimeEnabled = this.DefaultTimeEnabled.HasValue && this.DefaultTimeEnabled.Value;
            if (defaultTimeEnabled) {

                Boolean workdayWakeupTimeConfigured = this.DefaultTime != null && this.DefaultTime.HasValue;
                Boolean holidayWakeupTimeConfigured = this.DefaultTimeHolidays != null && this.DefaultTimeHolidays.HasValue;

                // add default wakeup times for today and tomorrow
                for (Int32 days = 0; days < 2; days++) {

                    DateTime day = localNow.Date.AddDays(days);

                    if (holidayWakeupTimeConfigured && this.TestIsHoliday(day, this.StartDateTimeHolidays))
                        starts.Add(day.Add(this.DefaultTimeHolidays.Value));

                    else if (holidayWakeupTimeConfigured && !this.TestIsWorkday(day, this.Workdays))
                        starts.Add(day.Add(this.DefaultTimeHolidays.Value));

                    else if (workdayWakeupTimeConfigured && this.TestIsWorkday(day, this.Workdays))
                        starts.Add(day.Add(this.DefaultTime.Value));

                }

            }

            // add appointments
            if (this.StartDateTime != null)
                foreach (DateTimeValueObject startDateTime in this.StartDateTime)
                    starts.Add(this.CalcWakeupDateTime(startDateTime, this.LeadTime));

            // find next wakeup time
            if (starts.Count() > 0) {

                DateTime localNextWakeupDateTime = starts
                    .Where(dt => dt > localNow)
                    .OrderBy(dt => dt)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .First();

                if (localNextWakeupDateTime > localNow)
                    if (!this.WakeupDateTime.HasValue || this.WakeupDateTime.Value != localNextWakeupDateTime)
                        this.WakeupDateTime.Value = localNextWakeupDateTime;

            }

            // configure periodic update
            if (this.UpdateToken != null)
                this.SchedulerService.Remove(this.UpdateToken);

            if (defaultTimeEnabled)
                this.UpdateToken = this.SchedulerService.InvokeIn(TimeSpan.FromHours(1), this.UpdateWakeupDateTime);
            else
                this.UpdateToken = null;

        }

        /// <summary>
        ///   Tests if a date is listed as a holiday.</summary>
        /// <param name="date">
        ///   A date.</param>
        /// <param name="holidays">
        ///   A list of holidays.</param>
        /// <returns>
        ///   <c>true</c> if a <paramref name="date"/> is enlisted as holiday, otherwise <c>false</c>.</returns>
        private Boolean TestIsHoliday(DateTime date, IList<DateTimeValueObject> holidays) {

            try {

                if (holidays == null)
                    return false;

                DateTime day = date.Date;
                foreach (DateTimeValueObject holiday in holidays)
                    if (holiday != null && holiday.HasValue && holiday.Value.Date == day)
                        return true;

                return false;

            }
            catch {

                return false;

            }

        }

        /// <summary>
        ///   Tests if a weekday is configured as workdays.</summary>
        /// <param name="date">
        ///   A date.</param>
        /// <param name="workdays">
        ///   A list of days (Monday --> Sunday) with workday configuration.</param>
        /// <returns>
        ///   <c>true</c> if a <paramref name="date"/> is configured as workday, otherwise <c>false</c>.</returns>
        private Boolean TestIsWorkday(DateTime date, IList<BoolValueObject> workdays) {

            try {

                // workdays ==> 0 = Monday .. 6 = Sunday
                // weekday ==> 0 = Sunday .. 6 = Saturday

                Int32 weekday = (Int32)date.DayOfWeek - 1;
                BoolValueObject workday = weekday < 0 ? workdays[6] : workdays[weekday];

                return workday.HasValue && workday.Value;

            }
            catch {

                return false;

            }

        }

        /// <summary>
        ///   Calculates the wakeup date and time for a specific port combination 
        ///   (start date time, Lead time).</summary>
        /// <param name="startDateTime">
        ///   The start date and time.</param>
        /// <param name="leadTime">
        ///   The lead time.</param>
        /// <returns>
        ///   The wakeup date and time or <see cref="DateTime.MinValue"/>.</returns>
        private DateTime CalcWakeupDateTime(DateTimeValueObject startDateTime, TimeSpanValueObject leadTime) {

            try {

                DateTime start = DateTime.MinValue;

                if (startDateTime != null && startDateTime.HasValue)
                    start = startDateTime.Value;

                TimeSpan lead = TimeSpan.Zero;
                if (leadTime != null && leadTime.HasValue)
                    lead = leadTime.Value;

                return start.Subtract(lead);

            }
            catch {

                return DateTime.MinValue;

            }

        }

    }

}
