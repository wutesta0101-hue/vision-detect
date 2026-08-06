// 應用進入點。
// 只做三件事：建立 Vue 實例、掛上 Pinia、載入全域樣式。

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './styles/theme.css'

createApp(App).use(createPinia()).mount('#app')
