using CAF.Infrastructure.Core;
using CAF.Infrastructure.Core.Data;
using CAF.Infrastructure.Core.Events;
using CAF.Infrastructure.Core.Exceptions;
using CAF.Infrastructure.Core.Plugins;
using CAF.WebSite.Application.Seedwork.Configuration;
using CAF.WebSite.Application.Services.Catalog;
using CAF.WebSite.Application.Services.Common;
using CAF.WebSite.Application.Services.Localization;
using CAF.WebSite.Application.Services.Orders;
using CAF.WebSite.Domain.Seedwork.Common;
using CAF.WebSite.Domain.Seedwork.Shop.Catalog;
using CAF.WebSite.Domain.Seedwork.Shop.Orders;
using CAF.WebSite.Domain.Seedwork.Shop.Shipping;
using CAF.WebSite.Domain.Seedwork.Users;
using CAF.WebSite.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
 

namespace CAF.WebSite.Application.Services.Shipping
{
    public partial class ShippingService : IShippingService
    {
        #region Fields

        private readonly IRepository<ShippingMethod> _shippingMethodRepository;
        private readonly ICacheManager _cacheManager;
        private readonly ILogger _logger;
        private readonly IProductAttributeParser _productAttributeParser;
		private readonly IProductService _productService;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
		private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly ShippingSettings _shippingSettings;
        private readonly IPluginFinder _pluginFinder;
        private readonly IEventPublisher _eventPublisher;
        private readonly ShoppingCartSettings _shoppingCartSettings;
		private readonly ISettingService _settingService;
		private readonly IProviderManager _providerManager;

        #endregion

        #region Ctor

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="cacheManager">Cache manager</param>
        /// <param name="shippingMethodRepository">Shipping method repository</param>
        /// <param name="logger">Logger</param>
        /// <param name="productAttributeParser">Product attribute parser</param>
		/// <param name="productService">Product service</param>
        /// <param name="checkoutAttributeParser">Checkout attribute parser</param>
		/// <param name="genericAttributeService">Generic attribute service</param>
        /// <param name="localizationService">Localization service</param>
        /// <param name="shippingSettings">Shipping settings</param>
        /// <param name="pluginFinder">Plugin finder</param>
        /// <param name="eventPublisher">Event published</param>
        /// <param name="shoppingCartSettings">Shopping cart settings</param>
		/// <param name="settingService">Setting service</param>
        public ShippingService(ICacheManager cacheManager, 
            IRepository<ShippingMethod> shippingMethodRepository,
            ILogger logger,
            IProductAttributeParser productAttributeParser,
			IProductService productService,
            ICheckoutAttributeParser checkoutAttributeParser,
			IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            ShippingSettings shippingSettings,
            IPluginFinder pluginFinder,
            IEventPublisher eventPublisher,
            ShoppingCartSettings shoppingCartSettings,
			ISettingService settingService,
			IProviderManager providerManager)
        {
            this._cacheManager = cacheManager;
            this._shippingMethodRepository = shippingMethodRepository;
            this._logger = logger;
            this._productAttributeParser = productAttributeParser;
			this._productService = productService;
            this._checkoutAttributeParser = checkoutAttributeParser;
			this._genericAttributeService = genericAttributeService;
            this._localizationService = localizationService;
            this._shippingSettings = shippingSettings;
            this._pluginFinder = pluginFinder;
            this._eventPublisher = eventPublisher;
            this._shoppingCartSettings = shoppingCartSettings;
			this._settingService = settingService;
			this._providerManager = providerManager;
		}

        #endregion
        
        #region Methods

        #region Shipping rate computation methods

        /// <summary>
        /// Load active shipping rate computation methods
        /// </summary>
		/// <param name="siteId">Load records allows only in specified store; pass 0 to load all records</param>
        /// <returns>Shipping rate computation methods</returns>
		public virtual IEnumerable<Provider<IShippingRateComputationMethod>> LoadActiveShippingRateComputationMethods(int siteId = 0)
        {
			var allMethods = LoadAllShippingRateComputationMethods(siteId);
			var activeMethods = allMethods
				.Where(p => p.Value.IsActive && _shippingSettings.ActiveShippingRateComputationMethodSystemNames.Contains(p.Metadata.SystemName, StringComparer.InvariantCultureIgnoreCase));

			if (!activeMethods.Any())
			{
				var fallbackMethod = allMethods.FirstOrDefault();
				if (fallbackMethod != null)
				{
					_shippingSettings.ActiveShippingRateComputationMethodSystemNames.Clear();
					_shippingSettings.ActiveShippingRateComputationMethodSystemNames.Add(fallbackMethod.Metadata.SystemName);
					_settingService.SaveSetting(_shippingSettings);
					return new Provider<IShippingRateComputationMethod>[] { fallbackMethod };
				}
				else
				{
					throw Error.Application("At least one shipping method provider is required to be active.");
				}
			}

			return activeMethods;
        }

        /// <summary>
        /// Load shipping rate computation method by system name
        /// </summary>
        /// <param name="systemName">System name</param>
        /// <returns>Found Shipping rate computation method</returns>
		public virtual Provider<IShippingRateComputationMethod> LoadShippingRateComputationMethodBySystemName(string systemName, int siteId = 0)
        {
			return _providerManager.GetProvider<IShippingRateComputationMethod>(systemName, siteId);
        }

        /// <summary>
        /// Load all shipping rate computation methods
        /// </summary>
		/// <param name="siteId">Load records allows only in specified store; pass 0 to load all records</param>
        /// <returns>Shipping rate computation methods</returns>
		public virtual IEnumerable<Provider<IShippingRateComputationMethod>> LoadAllShippingRateComputationMethods(int siteId = 0)
        {
			return _providerManager.GetAllProviders<IShippingRateComputationMethod>(siteId);
        }

        #endregion

        #region Shipping methods


        /// <summary>
        /// Deletes a shipping method
        /// </summary>
        /// <param name="shippingMethod">The shipping method</param>
        public virtual void DeleteShippingMethod(ShippingMethod shippingMethod)
        {
            if (shippingMethod == null)
                throw new ArgumentNullException("shippingMethod");

            _shippingMethodRepository.Delete(shippingMethod);

            //event notification
            _eventPublisher.EntityDeleted(shippingMethod);
        }

        /// <summary>
        /// Gets a shipping method
        /// </summary>
        /// <param name="shippingMethodId">The shipping method identifier</param>
        /// <returns>Shipping method</returns>
        public virtual ShippingMethod GetShippingMethodById(int shippingMethodId)
        {
            if (shippingMethodId == 0)
                return null;

            return _shippingMethodRepository.GetById(shippingMethodId);
        }
        
        /// <summary>
        /// Gets all shipping methods
        /// </summary>
        /// <param name="filterByCountryId">The country indentifier to filter by</param>
        /// <returns>Shipping method collection</returns>
        public virtual IList<ShippingMethod> GetAllShippingMethods(int? filterByCountryId = null)
        {
            if (filterByCountryId.HasValue && filterByCountryId.Value > 0)
            {
                var query1 = from sm in _shippingMethodRepository.Table
                             where
                             sm.RestrictedCountries.Select(c => c.Id).Contains(filterByCountryId.Value)
                             select sm.Id;

                var query2 = from sm in _shippingMethodRepository.Table
                             where !query1.Contains(sm.Id)
                             orderby sm.DisplayOrder
                             select sm;

                var shippingMethods = query2.ToList();
                return shippingMethods;
            }
            else
            {
                var query = from sm in _shippingMethodRepository.Table
                            orderby sm.DisplayOrder
                            select sm;
                var shippingMethods = query.ToList();
                return shippingMethods;
            }
        }

        /// <summary>
        /// Inserts a shipping method
        /// </summary>
        /// <param name="shippingMethod">Shipping method</param>
        public virtual void InsertShippingMethod(ShippingMethod shippingMethod)
        {
            if (shippingMethod == null)
                throw new ArgumentNullException("shippingMethod");

            _shippingMethodRepository.Insert(shippingMethod);

            //event notification
            _eventPublisher.EntityInserted(shippingMethod);
        }

        /// <summary>
        /// Updates the shipping method
        /// </summary>
        /// <param name="shippingMethod">Shipping method</param>
        public virtual void UpdateShippingMethod(ShippingMethod shippingMethod)
        {
            if (shippingMethod == null)
                throw new ArgumentNullException("shippingMethod");

            _shippingMethodRepository.Update(shippingMethod);

            //event notification
            _eventPublisher.EntityUpdated(shippingMethod);
        }

        #endregion

        #region Workflow

        /// <summary>
        /// Gets shopping cart item weight (of one item)
        /// </summary>
        /// <param name="shoppingCartItem">Shopping cart item</param>
        /// <returns>Shopping cart item weight</returns>
		public virtual decimal GetShoppingCartItemWeight(OrganizedShoppingCartItem shoppingCartItem)
        {
            if (shoppingCartItem == null)
                throw new ArgumentNullException("shoppingCartItem");

            decimal weight = decimal.Zero;

            if (shoppingCartItem.Item.Product != null)
            {
                decimal attributesTotalWeight = decimal.Zero;

                if (!String.IsNullOrEmpty(shoppingCartItem.Item.AttributesXml))
                {
                    var pvaValues = _productAttributeParser.ParseProductVariantAttributeValues(shoppingCartItem.Item.AttributesXml);

					foreach (var pvaValue in pvaValues)
					{
						if (pvaValue.ValueType == ProductVariantAttributeValueType.ProductLinkage)
						{
							var linkedProduct = _productService.GetProductById(pvaValue.LinkedProductId);
							if (linkedProduct != null && linkedProduct.IsShipEnabled)
								attributesTotalWeight += (linkedProduct.Weight * pvaValue.Quantity);
						}
						else
						{
							attributesTotalWeight += pvaValue.WeightAdjustment;
						}
					}
                }

                weight = shoppingCartItem.Item.Product.Weight + attributesTotalWeight;
            }
            return weight;
        }

        /// <summary>
        /// Gets shopping cart item total weight
        /// </summary>
        /// <param name="shoppingCartItem">Shopping cart item</param>
        /// <returns>Shopping cart item weight</returns>
		public virtual decimal GetShoppingCartItemTotalWeight(OrganizedShoppingCartItem shoppingCartItem)
        {
            if (shoppingCartItem == null)
                throw new ArgumentNullException("shoppingCartItem");

            decimal totalWeight = GetShoppingCartItemWeight(shoppingCartItem) * shoppingCartItem.Item.Quantity;
            return totalWeight;
        }

        /// <summary>
        /// Gets shopping cart weight
        /// </summary>
        /// <param name="cart">Cart</param>
        /// <returns>Shopping cart weight</returns>
		public virtual decimal GetShoppingCartTotalWeight(IList<OrganizedShoppingCartItem> cart)
        {
            User user = cart.GetUser();

            decimal totalWeight = decimal.Zero;
            //shopping cart items
            foreach (var shoppingCartItem in cart)
                totalWeight += GetShoppingCartItemTotalWeight(shoppingCartItem);

            //checkout attributes
            if (user != null)
            {
				var checkoutAttributesXml = user.GetAttribute<string>(SystemUserAttributeNames.CheckoutAttributes, _genericAttributeService);
				if (!String.IsNullOrEmpty(checkoutAttributesXml))
				{
					var caValues = _checkoutAttributeParser.ParseCheckoutAttributeValues(checkoutAttributesXml);
					foreach (var caValue in caValues)
						totalWeight += caValue.WeightAdjustment;
				}
            }
            return totalWeight;
        }

        /// <summary>
        /// Create shipment package from shopping cart
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="shippingAddress">Shipping address</param>
        /// <returns>Shipment package</returns>
		public virtual GetShippingOptionRequest CreateShippingOptionRequest(IList<OrganizedShoppingCartItem> cart,
            Address shippingAddress)
        {
            var request = new GetShippingOptionRequest();
            request.User = cart.GetUser();
            request.Items = new List<OrganizedShoppingCartItem>();
            foreach (var sc in cart)
                if (sc.Item.IsShipEnabled)
                    request.Items.Add(sc);
            request.ShippingAddress = shippingAddress;
            request.CountryFrom = null;
            request.StateProvinceFrom = null;
            request.ZipPostalCodeFrom = string.Empty;
            return request;

        }

        /// <summary>
        ///  Gets available shipping options
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="shippingAddress">Shipping address</param>
        /// <param name="allowedShippingRateComputationMethodSystemName">Filter by shipping rate computation method identifier; null to load shipping options of all shipping rate computation methods</param>
		/// <param name="siteId">Load records allows only in specified store; pass 0 to load all records</param>
        /// <returns>Shipping options</returns>
		public virtual GetShippingOptionResponse GetShippingOptions(IList<OrganizedShoppingCartItem> cart, Address shippingAddress, string allowedShippingRateComputationMethodSystemName = "", int siteId = 0)
        {
            if (cart == null)
                throw new ArgumentNullException("cart");

            var result = new GetShippingOptionResponse();
            
            //create a package
            var getShippingOptionRequest = CreateShippingOptionRequest(cart, shippingAddress);

            var shippingRateComputationMethods = LoadActiveShippingRateComputationMethods(siteId)
                .Where(srcm => 
                    String.IsNullOrWhiteSpace(allowedShippingRateComputationMethodSystemName) || 
                    allowedShippingRateComputationMethodSystemName.Equals(srcm.Metadata.SystemName, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            if (shippingRateComputationMethods.Count == 0)
                throw new WorkException("Shipping rate computation method could not be loaded");

            //get shipping options
            foreach (var srcm in shippingRateComputationMethods)
            {
                var getShippingOptionResponse = srcm.Value.GetShippingOptions(getShippingOptionRequest);
                foreach (var so2 in getShippingOptionResponse.ShippingOptions)
                {
                    //system name
                    so2.ShippingRateComputationMethodSystemName = srcm.Metadata.SystemName;

                    //round
                    if (_shoppingCartSettings.RoundPricesDuringCalculation)
                        so2.Rate = Math.Round(so2.Rate, 2);

                    result.ShippingOptions.Add(so2);
                }

                //log errors
                if (!getShippingOptionResponse.Success)
                {
                    foreach (string error in getShippingOptionResponse.Errors)
                    {
                        result.AddError(error);
						_logger.Warning(string.Format("Shipping ({0}). {1}", srcm.Metadata.FriendlyName, error));
                    }
                }
            }

            if (_shippingSettings.ReturnValidOptionsIfThereAreAny)
            {
                //return valid options if there are any (no matter of the errors returned by other shipping rate compuation methods).
                if (result.ShippingOptions.Count > 0 && result.Errors.Count > 0)
                    result.Errors.Clear();
            }
            
            //no shipping options loaded
            if (result.ShippingOptions.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add(_localizationService.GetResource("Checkout.ShippingOptionCouldNotBeLoaded"));
            
            return result;
        }

        #endregion

        #endregion
    }
}
