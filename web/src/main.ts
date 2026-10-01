import { createApp } from 'vue'
import Antd from 'ant-design-vue'
import 'ant-design-vue/dist/reset.css'
import App from './App.vue'
import { i18n } from './i18n'
import { router } from './router'
import './styles/main.css'

createApp(App).use(Antd).use(i18n).use(router).mount('#app')
