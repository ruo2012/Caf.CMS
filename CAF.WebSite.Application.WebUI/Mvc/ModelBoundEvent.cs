﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;

namespace CAF.WebSite.Application.WebUI.Mvc
{
	public class ModelBoundEvent
	{
		public ModelBoundEvent(TabbableModel boundModel, object entityModel, FormCollection form)
        {
			this.BoundModel = boundModel;
			this.EntityModel = entityModel;
			this.Form = form;
        }

		public TabbableModel BoundModel { get; private set; }
        public object EntityModel { get; private set; }
		public FormCollection Form { get; private set; }
	}
}
